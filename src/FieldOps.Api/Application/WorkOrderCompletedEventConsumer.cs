using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FieldOps.Api.Application;

// Day 68: the real payoff of Day 67's WorkOrderCompletedEvent — until this
// class existed, the event was published to RabbitMQ but nothing ever read
// it back, so it provided no actual behavior on its own. This BackgroundService
// (Day 50's WorkOrderReportCacheWarmer pattern, but triggered by RabbitMQ
// delivering a message rather than by a timer) is what finally lets
// WorkOrdersController.Complete stop sending the completion notification
// itself — Complete only publishes the fact that happened; deciding what
// to do about it now lives entirely here instead.
public class WorkOrderCompletedEventConsumer : BackgroundService
{
    private readonly string _hostName;
    private readonly INotificationSender _notificationSender;
    private readonly ILogger<WorkOrderCompletedEventConsumer> _logger;

    public WorkOrderCompletedEventConsumer(
        IConfiguration configuration,
        INotificationSender notificationSender,
        ILogger<WorkOrderCompletedEventConsumer> logger)
    {
        _hostName = configuration["RabbitMq:HostName"] ?? "localhost";
        _notificationSender = notificationSender;
        _logger = logger;
    }

    // Unlike RabbitMqEventPublisher's deliberate "new connection per call"
    // simplification, a consumer genuinely needs one long-lived connection
    // that stays open for as long as the app runs — there is no equivalent
    // of "open, do one thing, close" for something that must react to
    // messages arriving at unpredictable times.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IConnection? connection = null;
        IChannel? channel = null;
        try
        {
            var factory = new ConnectionFactory { HostName = _hostName };
            connection = await factory.CreateConnectionAsync(stoppingToken);
            channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

            var queueName = EventQueueNaming.QueueNameFor<WorkOrderCompletedEvent>();
            await channel.QueueDeclareAsync(
                queue: queueName, durable: false, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                try
                {
                    var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                    var domainEvent = JsonSerializer.Deserialize<WorkOrderCompletedEvent>(json);

                    if (domainEvent?.CustomerId is not null)
                    {
                        await _notificationSender.NotifyAsync(
                            $"Work order '{domainEvent.Title}' has been completed and is awaiting your approval.",
                            stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process a WorkOrderCompletedEvent message");
                }
                finally
                {
                    // Always ack, even if processing above failed — a
                    // permanently-failing notification must never cause
                    // RabbitMQ to redeliver the same message forever.
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

            // BasicConsumeAsync only registers the callback above; this
            // keeps ExecuteAsync (and therefore the connection/channel) alive
            // until the host itself shuts down.
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — the host is stopping.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WorkOrderCompletedEventConsumer could not connect to RabbitMQ; completed-work-order notifications will not be sent until the app restarts.");
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
