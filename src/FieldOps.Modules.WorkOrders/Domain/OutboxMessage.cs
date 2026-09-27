namespace FieldOps.Modules.WorkOrders.Domain;

// Day 71: the Outbox pattern's own table — deliberately a plain,
// event-agnostic row (a type name plus a JSON blob), not a strongly-typed
// WorkOrderCompletedEvent column. This module has no reference to
// FieldOps.Api's Application types at all (ADR 0002's same reasoning
// applied to events, not just cross-module facts) — it only ever stores
// and returns opaque strings the host constructed and will later
// deserialize itself.
internal class OutboxMessage
{
    public int Id { get; set; }
    public string EventType { get; set; }
    public string Payload { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    // null = not yet published. Set the moment OutboxPublisher (host-side)
    // confirms IEventPublisher.PublishAsync succeeded for this row.
    public DateTime? PublishedAtUtc { get; set; }

    public OutboxMessage(string eventType, string payload)
    {
        EventType = eventType;
        Payload = payload;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
