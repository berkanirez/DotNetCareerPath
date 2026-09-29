namespace FieldOps.NotificationService;

// Day 76: moved here from FieldOps.Api, per ADR 0005 — this service is now
// the only place a WorkOrderCompletedEvent turns into an actual
// notification. FieldOps.Api still publishes the event (via its Outbox
// pattern); this consumer no longer lives anywhere near that code.
public class WorkOrderCompletedEventConsumer : EventConsumerBase<WorkOrderCompletedEvent>
{
    private readonly INotificationSender _notificationSender;

    public WorkOrderCompletedEventConsumer(
        IConfiguration configuration,
        INotificationSender notificationSender,
        IServiceScopeFactory scopeFactory,
        ILogger<WorkOrderCompletedEventConsumer> logger)
        : base(configuration["RabbitMq:HostName"] ?? "localhost", "notifications", scopeFactory, logger)
    {
        _notificationSender = notificationSender;
    }

    protected override async Task HandleAsync(WorkOrderCompletedEvent domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.CustomerId is not null)
        {
            await _notificationSender.NotifyAsync(
                $"Work order '{domainEvent.Title}' has been completed and is awaiting your approval.",
                cancellationToken);
        }
    }
}
