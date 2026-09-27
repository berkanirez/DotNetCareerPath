namespace FieldOps.Api.Application;

// Day 67: same seam shape as Day 51's INotificationSender and Day 63's
// IAiProvider — business code (WorkOrdersController) depends only on this
// interface, never on RabbitMQ directly. Generic over TEvent rather than
// tied to WorkOrderCompletedEvent specifically, so a later, different
// domain event doesn't need a second, near-identical interface.
public interface IEventPublisher
{
    // Day 73: messageId is a stable, unique identifier for THIS specific
    // message (today: the outbox row's own Id) — carried through to
    // RabbitMQ so a consumer receiving the same message twice (RabbitMQ's
    // own "at-least-once," never "exactly-once," delivery guarantee) can
    // recognize the duplicate via the Inbox pattern instead of blindly
    // reprocessing it.
    Task PublishAsync<TEvent>(TEvent domainEvent, string messageId, CancellationToken cancellationToken);
}
