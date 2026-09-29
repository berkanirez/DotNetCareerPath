using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FieldOps.NotificationService;

// Day 76: this service's own copy of FieldOps.Api's EventConsumerBase<TEvent>
// (Day 69-74) — the connect/retry/inbox/dead-letter mechanics are identical
// on purpose (the same RabbitMQ contract applies to any consumer, in any
// process), but this copy depends only on THIS project's own IInboxStore,
// never on FieldOps.Api or any of its modules. Duplicating this class,
// rather than sharing one project reference, is exactly what makes this a
// real, independently-deployable service instead of a distributed monolith.
public abstract class EventConsumerBase<TEvent> : BackgroundService
{
    private readonly string _hostName;
    private readonly string _consumerName;
    private readonly IServiceScopeFactory _scopeFactory;
    protected readonly ILogger Logger;

    protected EventConsumerBase(string hostName, string consumerName, IServiceScopeFactory scopeFactory, ILogger logger)
    {
        _hostName = hostName;
        _consumerName = consumerName;
        _scopeFactory = scopeFactory;
        Logger = logger;
    }

    protected abstract Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);

    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);
    private const int MaxDeliveryAttempts = 3;

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

                var queueName = EventQueueNaming.QueueNameFor<TEvent>(_consumerName);
                await channel.QueueDeclareAsync(
                    queue: queueName, durable: false, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
                await channel.QueueBindAsync(
                    queue: queueName, exchange: exchangeName, routingKey: string.Empty, cancellationToken: stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, ea) =>
                {
                    var messageId = ea.BasicProperties.MessageId;
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

                retryDelay = InitialRetryDelay;

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
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
}
