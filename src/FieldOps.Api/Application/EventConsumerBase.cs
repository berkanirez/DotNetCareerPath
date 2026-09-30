using System.Diagnostics;
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

    // Day 74: a message that keeps failing THIS many times (not from a
    // RabbitMQ outage — that's the retry loop above — but from HandleAsync
    // itself repeatedly throwing) is moved to a dead-letter queue instead
    // of being requeued forever.
    private const int MaxDeliveryAttempts = 3;

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
                    // Day 73: the Inbox pattern's read side — RabbitMQ's
                    // "at-least-once" guarantee means this exact message
                    // could, in principle, be delivered again (e.g. if a
                    // previous delivery's ack never made it back before
                    // a crash). MessageId was set by RabbitMqEventPublisher
                    // to the outbox row's own stable Id.
                    var messageId = ea.BasicProperties.MessageId;

                    // Day 85: pick up the SAME trace the publisher started
                    // (RabbitMqEventPublisher's Inject call), instead of
                    // this consumer's work looking like an unrelated,
                    // brand-new trace with no connection to whatever
                    // published this message.
                    using var activity = StartConsumerActivity(ea.BasicProperties.Headers);

                    try
                    {
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

                        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "{ConsumerName} failed to process a {EventType} message", _consumerName, typeof(TEvent).Name);

                        // Day 74: a genuinely broken/unprocessable message
                        // must not be retried forever — bound it, then
                        // dead-letter it. This whole block is deliberately
                        // its own try/catch: whatever happens here must
                        // never escape ReceivedAsync unhandled.
                        try
                        {
                            await HandleDeliveryFailureAsync(channel, ea, messageId, stoppingToken);
                        }
                        catch (Exception handlingEx)
                        {
                            Logger.LogWarning(handlingEx, "{ConsumerName} failed to handle its own delivery failure for a {EventType} message", _consumerName, typeof(TEvent).Name);
                        }
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

    // Day 74: decides, for a message whose processing just failed, whether
    // to give it another chance (NACK + requeue — RabbitMQ redelivers it
    // immediately) or to give up on it (publish a copy to a dedicated
    // dead-letter queue, then ACK the original so it stops circulating in
    // the normal queue). A message with no MessageId at all can't have its
    // attempts tracked, so it fails open (acked, not dead-lettered) rather
    // than being retried forever with no way to ever count it as exhausted.
    private async Task HandleDeliveryFailureAsync(IChannel channel, BasicDeliverEventArgs ea, string? messageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(messageId))
        {
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();
        var attemptCount = inboxStore.RecordFailedAttempt(_consumerName, messageId);

        if (attemptCount < MaxDeliveryAttempts)
        {
            Logger.LogWarning(
                "{ConsumerName} will retry {EventType} message {MessageId} (attempt {AttemptCount} of {MaxDeliveryAttempts})",
                _consumerName, typeof(TEvent).Name, messageId, attemptCount, MaxDeliveryAttempts);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
        }
        else
        {
            Logger.LogWarning(
                "{ConsumerName} exhausted {MaxDeliveryAttempts} attempts for {EventType} message {MessageId}; moving it to the dead-letter queue",
                _consumerName, MaxDeliveryAttempts, typeof(TEvent).Name, messageId);

            var deadLetterQueueName = EventQueueNaming.DeadLetterQueueNameFor<TEvent>(_consumerName);
            await channel.QueueDeclareAsync(
                queue: deadLetterQueueName, durable: false, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: deadLetterQueueName,
                mandatory: false,
                basicProperties: new BasicProperties { MessageId = messageId },
                body: ea.Body,
                cancellationToken: cancellationToken);

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
    }

    // Day 85: the consumer-side half of manual trace context propagation.
    // RabbitMQ header VALUES round-trip as byte[] once a message has
    // actually been delivered (even though we wrote a plain string on the
    // publish side) — this getter has to decode that back to UTF8 text
    // before DistributedContextPropagator can parse it as a traceparent.
    private static Activity? StartConsumerActivity(IDictionary<string, object?>? headers)
    {
        DistributedContextPropagator.Current.ExtractTraceIdAndState(
            headers,
            static (object? carrier, string fieldName, out string? fieldValue, out IEnumerable<string>? fieldValues) =>
            {
                fieldValues = null;
                fieldValue = null;
                if (carrier is IDictionary<string, object?> dict && dict.TryGetValue(fieldName, out var value))
                {
                    fieldValue = value switch
                    {
                        byte[] bytes => Encoding.UTF8.GetString(bytes),
                        string s => s,
                        _ => null
                    };
                }
            },
            out var traceParent,
            out var traceState);

        var parentContext = default(ActivityContext);
        if (!string.IsNullOrEmpty(traceParent))
        {
            ActivityContext.TryParse(traceParent, traceState, out parentContext);
        }

        // If no valid parent was found (e.g. an old message with no
        // headers), this still starts a brand-new, unparented trace rather
        // than silently skipping tracing altogether.
        return FieldOpsTracing.MessagingSource.StartActivity(
            $"consume {typeof(TEvent).Name}", ActivityKind.Consumer, parentContext);
    }
}
