namespace FieldOps.Modules.WorkOrders;

// Day 71: the public shape of an outbox row — same "public DTO, internal
// entity" split as WorkOrderSummary/WorkOrder. The host (OutboxPublisher)
// only ever sees this: an opaque EventType name plus a JSON Payload it will
// deserialize itself, never the module's internal OutboxMessage entity.
//
// Day 86: CreatedAtUtc added — OutboxPublisher needs it to measure "outbox
// lag" (how long a message waited between being written and actually being
// published), a real production metric. Still an opaque timestamp, not
// anything WorkOrder-specific — ADR 0002's boundary is untouched.
public record OutboxMessageSummary(int Id, string EventType, string Payload, DateTime CreatedAtUtc);
