namespace FieldOps.Api.Application;

// Day 68: the real payoff of Day 67's WorkOrderCompletedEvent — until this
// class existed, the event was published to RabbitMQ but nothing ever read
// it back, so it provided no actual behavior on its own. WorkOrdersController.
// Complete only publishes the fact that happened; deciding what to do about
// it now lives entirely here instead.
//
// Day 69: rebuilt on top of EventConsumerBase<TEvent> once
// WorkOrderCompletedAuditConsumer needed the exact same RabbitMQ mechanics
// — this class now only says WHAT to do with the event ("notifications"
// is its own queue name/consumer identity), not HOW to connect/consume.
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
