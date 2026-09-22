namespace FieldOps.Modules.AuditLogs.Domain;

// internal — same ADR 0001 pattern as every other module's domain entity.
// Deliberately minimal: no "Details" JSON blob today, no read/query API
// (IAuditLogWriter only writes) — this is an append-only ledger, not a
// reporting feature yet.
internal class AuditLogEntry
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int WorkOrderId { get; set; }
    public string Action { get; set; }
    public string ActorType { get; set; }
    public int ActorId { get; set; }
    public DateTime OccurredAtUtc { get; set; }

    public AuditLogEntry(int organizationId, int workOrderId, string action, string actorType, int actorId, DateTime occurredAtUtc)
    {
        OrganizationId = organizationId;
        WorkOrderId = workOrderId;
        Action = action;
        ActorType = actorType;
        ActorId = actorId;
        OccurredAtUtc = occurredAtUtc;
    }
}
