namespace FieldOps.Api.Application;

// Day 68: both RabbitMqEventPublisher (Day 67) and WorkOrderCompletedEventConsumer
// (today) must agree on the exact same queue name for a given event type —
// kept in one shared place so a publisher and a consumer can never
// silently drift onto two different queue names.
public static class EventQueueNaming
{
    public static string QueueNameFor<TEvent>() => $"fieldops.{typeof(TEvent).Name}";
}
