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
// RabbitMQ (or, since Day 80, Elasticsearch) was completely unreachable at
// the moment WorkOrdersController.Create/Complete ran, the outbox rows they
// wrote still exist, unpublished, in the database — this loop will keep
// finding and retrying them, tick after tick, until each one eventually
// succeeds. Nothing is lost to a transient outage of either dependency.
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
        var workOrderSearchIndex = scope.ServiceProvider.GetRequiredService<IWorkOrderSearchIndex>();

        foreach (var message in workOrderDirectory.GetUnpublishedOutboxMessages())
        {
            try
            {
                // Day 80: a second known EventType, alongside Day 71's
                // original one — a real, general-purpose outbox would look
                // this up in a type registry instead of if/else-if chain;
                // not justified yet for just two.
                if (message.EventType == nameof(WorkOrderCompletedEvent))
                {
                    var domainEvent = JsonSerializer.Deserialize<WorkOrderCompletedEvent>(message.Payload);
                    if (domainEvent is not null)
                    {
                        // Day 73: the outbox row's own Id is already a stable,
                        // unique identifier for this exact message — reused
                        // as-is rather than minting a separate Guid.
                        await eventPublisher.PublishAsync(domainEvent, message.Id.ToString(), cancellationToken);
                        workOrderDirectory.MarkOutboxMessagePublished(message.Id);
                        RecordPublishLag(message.CreatedAtUtc);
                    }
                }
                else if (message.EventType == nameof(WorkOrderSearchDocument))
                {
                    // Day 80: the exact same tolerance the Outbox pattern
                    // already gave RabbitMQ (Day 71), now for Elasticsearch —
                    // if IndexAsync throws (Elasticsearch unreachable), this
                    // row is simply never marked published, and this same
                    // loop retries it on the next tick.
                    var document = JsonSerializer.Deserialize<WorkOrderSearchDocument>(message.Payload);
                    if (document is not null)
                    {
                        await workOrderSearchIndex.IndexAsync(document, cancellationToken);
                        workOrderDirectory.MarkOutboxMessagePublished(message.Id);
                        RecordPublishLag(message.CreatedAtUtc);
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

    // Day 86: recorded only on genuine success — a message that's still
    // sitting in the outbox because Elasticsearch/RabbitMQ is down should
    // NOT count toward this histogram yet; its eventual, larger lag gets
    // recorded once it actually succeeds, on whatever later tick that is.
    private static void RecordPublishLag(DateTime createdAtUtc)
    {
        var lag = DateTime.UtcNow - createdAtUtc;
        FieldOpsMetrics.OutboxPublishLagSeconds.Record(lag.TotalSeconds);
    }
}
