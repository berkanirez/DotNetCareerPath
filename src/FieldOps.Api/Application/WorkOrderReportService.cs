using System.Text.Json;
using FieldOps.Api.Models;
using FieldOps.Modules.WorkOrders;
using StackExchange.Redis;

namespace FieldOps.Api.Application;

// Day 48 (Redis, first step): cache-aside — check Redis first, compute and
// store on a miss, return on a hit. Deliberately TTL-only today (no active
// invalidation when a work order's status changes) — a real, documented
// limitation, not an oversight: an event-driven invalidation step is
// planned as a follow-up, once this basic read-through shape is proven live.
public class WorkOrderReportService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly IWorkOrderDirectory _workOrderDirectory;
    private readonly IConnectionMultiplexer _redis;

    public WorkOrderReportService(IWorkOrderDirectory workOrderDirectory, IConnectionMultiplexer redis)
    {
        _workOrderDirectory = workOrderDirectory;
        _redis = redis;
    }

    public WorkOrderStatusReport GetStatusReport(int organizationId)
    {
        var db = _redis.GetDatabase();
        var cacheKey = $"workorders:report:{organizationId}";

        var cached = db.StringGet(cacheKey);
        if (cached.HasValue)
        {
            return JsonSerializer.Deserialize<WorkOrderStatusReport>((string)cached!)!;
        }

        var workOrders = _workOrderDirectory.GetAll()
            .Where(w => w.OrganizationId == organizationId)
            .ToList();

        var report = new WorkOrderStatusReport(
            organizationId,
            Open: workOrders.Count(w => w.Status == WorkOrderStatus.Open),
            Assigned: workOrders.Count(w => w.Status == WorkOrderStatus.Assigned),
            InProgress: workOrders.Count(w => w.Status == WorkOrderStatus.InProgress),
            Completed: workOrders.Count(w => w.Status == WorkOrderStatus.Completed));

        db.StringSet(cacheKey, JsonSerializer.Serialize(report), CacheDuration);
        return report;
    }

    // Day 49: active invalidation — called by every controller action that
    // changes a work order's Status (the only thing this report counts).
    // Reassign/Approve never touch Status, so they never call this.
    public void InvalidateCache(int organizationId)
    {
        var db = _redis.GetDatabase();
        db.KeyDelete($"workorders:report:{organizationId}");
    }
}
