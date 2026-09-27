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
    protected readonly ILogger Logger;

    protected EventConsumerBase(string hostName, string consumerName, ILogger logger)
    {
        _hostName = hostName;
        _consumerName = consumerName;
        Logger = logger;
    }

    // The one thing each concrete consumer actually differs on — everything
    // about getting an event message to this point is identical for all of them.
    protected abstract Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
                    var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                    var domainEvent = JsonSerializer.Deserialize<TEvent>(json);
                    if (domainEvent is not null)
                    {
                        await HandleAsync(domainEvent, stoppingToken);
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

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — the host is stopping.
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{ConsumerName} could not connect to RabbitMQ; it will not receive {EventType} messages until the app restarts.", _consumerName, typeof(TEvent).Name);
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
