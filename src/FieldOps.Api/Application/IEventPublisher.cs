namespace FieldOps.Api.Application;

// Day 67: same seam shape as Day 51's INotificationSender and Day 63's
// IAiProvider — business code (WorkOrdersController) depends only on this
// interface, never on RabbitMQ directly. Generic over TEvent rather than
// tied to WorkOrderCompletedEvent specifically, so a later, different
// domain event doesn't need a second, near-identical interface.
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken);
}
