namespace FieldOps.Modules.AuditLogs;

// Day 52: an append-only "who/what/when" ledger. Write-only today — no
// GetAll/GetById, unlike every other module's directory interface, because
// there is no read/reporting feature for this data yet (deliberately
// deferred, same as ICustomerDirectory's still-missing Create on Day 47).
public interface IAuditLogWriter
{
    void Record(int organizationId, int workOrderId, string action, string actorType, int actorId);
}
