namespace FieldOps.Api.Application;

// Day 69: the concrete proof that the fanout exchange actually works — a
// second, entirely independent consumer of the SAME WorkOrderCompletedEvent,
// with its own queue, receiving its own copy of every message alongside
// WorkOrderCompletedEventConsumer. It does nothing "useful" on purpose
// (just logs); a real system might use this shape for a reporting service
// or a search-index updater. Neither consumer knows the other exists.
public class WorkOrderCompletedAuditConsumer : EventConsumerBase<WorkOrderCompletedEvent>
{
    public WorkOrderCompletedAuditConsumer(IConfiguration configuration, IServiceScopeFactory scopeFactory, ILogger<WorkOrderCompletedAuditConsumer> logger)
        : base(configuration["RabbitMq:HostName"] ?? "localhost", "audit", scopeFactory, logger)
    {
    }

    protected override Task HandleAsync(WorkOrderCompletedEvent domainEvent, CancellationToken cancellationToken)
    {
        Logger.LogInformation(
            "Audit: work order {WorkOrderId} ('{Title}') completed at {CompletedAtUtc}",
            domainEvent.WorkOrderId, domainEvent.Title, domainEvent.CompletedAtUtc);
        return Task.CompletedTask;
    }
}
