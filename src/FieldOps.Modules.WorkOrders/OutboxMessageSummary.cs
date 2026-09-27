namespace FieldOps.Modules.WorkOrders;

// Day 71: the public shape of an outbox row — same "public DTO, internal
// entity" split as WorkOrderSummary/WorkOrder. The host (OutboxPublisher)
// only ever sees this: an opaque EventType name plus a JSON Payload it will
// deserialize itself, never the module's internal OutboxMessage entity.
public record OutboxMessageSummary(int Id, string EventType, string Payload);
