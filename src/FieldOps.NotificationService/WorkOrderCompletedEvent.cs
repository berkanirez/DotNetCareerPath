namespace FieldOps.NotificationService;

// Day 76: deliberately a SEPARATE copy of FieldOps.Api's
// WorkOrderCompletedEvent (Day 67), not a shared reference — ADR 0005's
// whole point is that this service has no compile-time dependency on
// FieldOps.Api at all. Both sides simply agree, independently, on the same
// JSON shape; RabbitMQ never checks that agreement, so if one side's shape
// ever drifts from the other's, this record is the seam where that surfaces.
// Per ADR 0005's own "deferred" consequence, WHERE a real shared contract
// type (e.g. a FieldOps.Contracts project) would live is not decided today
// — this is the honestly-duplicated stand-in until that's needed.
public record WorkOrderCompletedEvent(
    int WorkOrderId,
    int OrganizationId,
    int? CustomerId,
    string Title,
    DateTime CompletedAtUtc);
