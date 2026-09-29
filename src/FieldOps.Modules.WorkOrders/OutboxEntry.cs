namespace FieldOps.Modules.WorkOrders;

// Day 80: lets a single mutation (Create, Complete) write MORE THAN ONE
// outbox row atomically, in the same SaveChanges call — e.g. Complete now
// needs both a WorkOrderCompletedEvent row (for RabbitMQ) and a
// search-index-request row (for Elasticsearch), and both must land
// together or neither does, the same guarantee Day 71 established for a
// single row. EventType/Payload stay just as opaque to this module as
// before — it never interprets either string.
public record OutboxEntry(string EventType, string Payload);
