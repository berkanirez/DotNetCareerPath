namespace FieldOps.NotificationService;

// Day 76: a second, independent copy of FieldOps.Api's EventQueueNaming
// (Day 69) — the ONLY thing that actually connects a publisher and a
// consumer is this exact same computed string, never a C# reference. Both
// sides must compute the identical exchange/queue name for the fanout
// binding to line up; that's exactly why RabbitMqEventPublisher (in
// FieldOps.Api) and this service duplicate this class instead of sharing
// one, per ADR 0005.
internal static class EventQueueNaming
{
    public static string ExchangeNameFor<TEvent>() => $"fieldops.events.{typeof(TEvent).Name}";

    public static string QueueNameFor<TEvent>(string consumerName) => $"{ExchangeNameFor<TEvent>()}.{consumerName}";

    public static string DeadLetterQueueNameFor<TEvent>(string consumerName) =>
        $"{QueueNameFor<TEvent>(consumerName)}.dead-letter";
}
