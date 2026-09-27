namespace FieldOps.Api.Application;

// Day 68: both a publisher and every consumer must agree on the exact same
// names for a given event type — kept in one shared place so they can
// never silently drift apart.
//
// Day 69: Day 67/68's single shared queue per event type is replaced with
// a real exchange + one queue per CONSUMER. A fanout exchange delivers a
// copy of every message to every queue bound to it — this is what actually
// lets more than one independent consumer (Day 69: notifications AND
// audit) receive the same event, which a single shared queue never could
// (RabbitMQ would have split messages between competing consumers on one
// queue, not duplicated them).
public static class EventQueueNaming
{
    public static string ExchangeNameFor<TEvent>() => $"fieldops.events.{typeof(TEvent).Name}";

    // consumerName makes each consumer's own queue unique — e.g.
    // "fieldops.events.WorkOrderCompletedEvent.notifications" and
    // "...audit", both bound to the same fanout exchange above.
    public static string QueueNameFor<TEvent>(string consumerName) =>
        $"{ExchangeNameFor<TEvent>()}.{consumerName}";
}
