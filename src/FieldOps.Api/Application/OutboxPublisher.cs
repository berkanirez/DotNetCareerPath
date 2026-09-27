using System.Text.Json;
using FieldOps.Modules.WorkOrders;

namespace FieldOps.Api.Application;

// Day 71: the read side of the Outbox pattern — same BackgroundService/
// PeriodicTimer/IServiceScopeFactory shape as Day 50's
// WorkOrderReportCacheWarmer, for the exact same reason: IWorkOrderDirectory
// and IEventPublisher are both Scoped/request-lifetime-shaped services, and
// this Singleton BackgroundService needs a fresh scope each tick rather than
// holding either directly.
//
// This is what actually delivers on the Outbox pattern's promise: even if
// RabbitMQ was completely unreachable at the moment WorkOrdersController.Complete
// ran, the outbox row it wrote still exists, unpublished, in the database —
// this loop will keep finding and retrying it, tick after tick, until it
// eventually succeeds. Nothing is lost to a transient RabbitMQ outage.
public class OutboxPublisher : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxPublisher> _logger;

    public OutboxPublisher(IServiceScopeFactory scopeFactory, ILogger<OutboxPublisher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // Day 52's lesson applied from the start this time: the ENTIRE
            // tick is wrapped, not just the publish call — a single bad tick
            // (e.g. a transient DB error resolving the scope) must never be
            // allowed to escape ExecuteAsync and take down the whole host.
            try
            {
                await PublishUnpublishedMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outbox publishing tick failed");
            }
        }
    }

    private async Task PublishUnpublishedMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var workOrderDirectory = scope.ServiceProvider.GetRequiredService<IWorkOrderDirectory>();
        var eventPublisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        foreach (var message in workOrderDirectory.GetUnpublishedOutboxMessages())
        {
            try
            {
                // Day 71's only known event type — a real, general-purpose
                // outbox would look this up in a type registry instead of a
                // single if-check; not justified yet for one event type.
                if (message.EventType == nameof(WorkOrderCompletedEvent))
                {
                    var domainEvent = JsonSerializer.Deserialize<WorkOrderCompletedEvent>(message.Payload);
                    if (domainEvent is not null)
                    {
                        await eventPublisher.PublishAsync(domainEvent, cancellationToken);
                        workOrderDirectory.MarkOutboxMessagePublished(message.Id);
                    }
                }
                else
                {
                    _logger.LogWarning("Outbox message {OutboxMessageId} has unrecognized EventType {EventType}", message.Id, message.EventType);
                }
            }
            catch (Exception ex)
            {
                // One message's publish failure (e.g. RabbitMQ still
                // unreachable) must not stop the rest of this tick's
                // messages, and must NOT mark this one published — it stays
                // in the outbox, unpublished, and this same loop will try it
                // again on the next tick.
                _logger.LogWarning(ex, "Failed to publish outbox message {OutboxMessageId}", message.Id);
            }
        }
    }
}
