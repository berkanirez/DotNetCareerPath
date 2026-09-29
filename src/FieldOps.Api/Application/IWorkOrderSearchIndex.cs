namespace FieldOps.Api.Application;

// Day 79: same seam shape as Day 51's INotificationSender and Day 63's
// IAiProvider — WorkOrdersController only ever depends on this interface,
// never on Elasticsearch's own client types directly. A different search
// backend (or, in tests, a no-op) could replace ElasticsearchWorkOrderSearchIndex
// with zero change to the controller.
public interface IWorkOrderSearchIndex
{
    Task IndexAsync(WorkOrderSearchDocument document, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkOrderSearchDocument>> SearchAsync(int organizationId, string query, CancellationToken cancellationToken);

    // Day 81: idempotent — safe to call every startup, unlike this repo's
    // SQL Server migrations (which stay deliberately manual). If the
    // "workorders" index already exists, this does nothing; if it doesn't
    // (a brand-new Elasticsearch, or one that was wiped), it's created with
    // an EXPLICIT mapping this time — Day 79 relied on dynamic mapping,
    // which guessed reasonably but wasn't something we actually decided.
    Task EnsureIndexExistsAsync(CancellationToken cancellationToken);

    // Day 81: the actual "rebuild strategy" — deliberately scoped to ONE
    // organization's own documents, not the whole index. A naive "delete
    // the whole index, recreate it" would wipe every OTHER organization's
    // search data too, just because one organization's admin asked for a
    // rebuild — breaking the same tenant-isolation rule Day 40 established
    // for every other operation in this controller.
    Task RebuildOrganizationIndexAsync(int organizationId, IReadOnlyList<WorkOrderSearchDocument> documents, CancellationToken cancellationToken);
}
