using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FieldOps.Api.Application;

// Day 69: extracted from Day 68's WorkOrderCompletedEventConsumer once a
// second consumer (WorkOrderCompletedAuditConsumer) needed the exact same
// connect/declare/bind/consume/ack boilerplate — only the one thing that's
// genuinely different per consumer (what to actually DO with an event) is
// left abstract. Generic over TEvent so a future, different domain event
// could reuse this same base without copying the RabbitMQ mechanics again.
public abstract class EventConsumerBase<TEvent> : BackgroundService
{
    private readonly string _hostName;
    private readonly string _consumerName;
    private readonly IServiceScopeFactory _scopeFactory;
    protected readonly ILogger Logger;

    // Day 73: IServiceScopeFactory (not IInboxStore directly) — same Day 50
    // reasoning as everywhere else it appears in this codebase:
    // IInboxStore's only implementation is backed by a Scoped
    // IWorkOrderDirectory, and this class itself is a Singleton
    // (BackgroundService), so a fresh scope has to be created per message.
    protected EventConsumerBase(string hostName, string consumerName, IServiceScopeFactory scopeFactory, ILogger logger)
    {
        _hostName = hostName;
        _consumerName = consumerName;
        _scopeFactory = scopeFactory;
        Logger = logger;
    }

    // The one thing each concrete consumer actually differs on — everything
    // about getting an event message to this point is identical for all of them.
    protected abstract Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);

    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    // Day 72: live-caught, real consequence of Day 68/69's "no reconnect
    // logic" gap — Day 71's live demo showed OutboxPublisher successfully
    // publish an event that no consumer ever received, because both
    // consumers had already given up (permanently) before RabbitMQ was even
    // ready. The whole connect-declare-bind-consume sequence is now wrapped
    // in an outer retry loop with exponential backoff: any failure —
    // whether the very first connection attempt, or a connection lost later
    // — sends control back to the top of the loop instead of letting
    // ExecuteAsync return and the consumer go silent for good.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retryDelay = InitialRetryDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            IConnection? connection = null;
            IChannel? channel = null;
            try
            {
                var factory = new ConnectionFactory { HostName = _hostName };
                connection = await factory.CreateConnectionAsync(stoppingToken);
                channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

                var exchangeName = EventQueueNaming.ExchangeNameFor<TEvent>();
                await channel.ExchangeDeclareAsync(
                    exchange: exchangeName, type: ExchangeType.Fanout, durable: false, autoDelete: false, cancellationToken: stoppingToken);

                // Each consumer gets its OWN queue (named after itself), bound
                // to the shared exchange — this is what lets it receive its own
                // copy of every message, independently of any other consumer's
                // queue for the same event type.
                var queueName = EventQueueNaming.QueueNameFor<TEvent>(_consumerName);
                await channel.QueueDeclareAsync(
                    queue: queueName, durable: false, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
                await channel.QueueBindAsync(
                    queue: queueName, exchange: exchangeName, routingKey: string.Empty, cancellationToken: stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, ea) =>
                {
                    try
                    {
                        // Day 73: the Inbox pattern's read side — RabbitMQ's
                        // "at-least-once" guarantee means this exact message
                        // could, in principle, be delivered again (e.g. if a
                        // previous delivery's ack never made it back before
                        // a crash). MessageId was set by RabbitMqEventPublisher
                        // to the outbox row's own stable Id.
                        var messageId = ea.BasicProperties.MessageId;
                        using var scope = _scopeFactory.CreateScope();
                        var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();

                        if (!string.IsNullOrEmpty(messageId) && inboxStore.HasProcessed(_consumerName, messageId))
                        {
                            Logger.LogInformation(
                                "{ConsumerName} skipping already-processed {EventType} message {MessageId}",
                                _consumerName, typeof(TEvent).Name, messageId);
                        }
                        else
                        {
                            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                            var domainEvent = JsonSerializer.Deserialize<TEvent>(json);
                            if (domainEvent is not null)
                            {
                                await HandleAsync(domainEvent, stoppingToken);
                            }

                            // Only recorded AFTER HandleAsync genuinely
                            // succeeds — the same "mark done only once the
                            // real work is done" discipline as
                            // OutboxPublisher's MarkOutboxMessagePublished.
                            if (!string.IsNullOrEmpty(messageId))
                            {
                                inboxStore.MarkProcessed(_consumerName, messageId);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "{ConsumerName} failed to process a {EventType} message", _consumerName, typeof(TEvent).Name);
                    }
                    finally
                    {
                        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                    }
                };

                await channel.BasicConsumeAsync(
                    queue: queueName,
                    autoAck: false,
                    consumerTag: string.Empty,
                    noLocal: false,
                    exclusive: false,
                    arguments: null,
                    consumer: consumer,
                    cancellationToken: stoppingToken);

                // Connected and consuming — a future disconnect deserves the
                // same quick first retry as a brand-new startup would, not
                // whatever long delay a previous failure streak had grown to.
                retryDelay = InitialRetryDelay;

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown — the host is stopping.
                break;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    ex,
                    "{ConsumerName} could not connect to (or lost its connection to) RabbitMQ; retrying in {RetryDelaySeconds}s",
                    _consumerName, retryDelay.TotalSeconds);

                try
                {
                    await Task.Delay(retryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // Exponential backoff, capped — doubling the wait after each
                // consecutive failure instead of hammering RabbitMQ with an
                // immediate retry every time, but never waiting forever
                // between attempts either.
                var doubledSeconds = retryDelay.TotalSeconds * 2;
                retryDelay = TimeSpan.FromSeconds(Math.Min(doubledSeconds, MaxRetryDelay.TotalSeconds));
            }
            finally
            {
                if (channel is not null)
                {
                    await channel.DisposeAsync();
                }

                if (connection is not null)
                {
                    await connection.DisposeAsync();
                }
            }
        }
    }
}
