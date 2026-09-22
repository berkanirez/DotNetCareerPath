using FieldOps.Modules.Organizations;

namespace FieldOps.Api.Application;

// Day 50: proactive cache warming. WorkOrderReportService.GetStatusReport is
// already cache-aside (Day 48) — a hit does nothing, a miss recomputes and
// re-caches. This background service just calls that same method on a
// schedule, for every organization, so a real user request almost never has
// to pay the cold-cache cost itself.
//
// A BackgroundService is registered as a Singleton (it lives for the whole
// app's lifetime), but WorkOrderReportService and IOrganizationDirectory are
// Scoped — a Singleton is not allowed to hold a Scoped dependency directly
// (proven live: constructor-injecting them here made the app fail to even
// start, with "Cannot consume scoped service ... from singleton
// IHostedService"). IServiceScopeFactory is itself a Singleton-safe service
// whose only job is to hand out a fresh scope on demand — one new scope per
// tick, disposed right after, exactly like ASP.NET Core creates one per HTTP
// request under the hood.
public class WorkOrderReportCacheWarmer : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkOrderReportCacheWarmer> _logger;

    public WorkOrderReportCacheWarmer(IServiceScopeFactory scopeFactory, ILogger<WorkOrderReportCacheWarmer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            WarmAllOrganizations();
        }
    }

    private void WarmAllOrganizations()
    {
        using var scope = _scopeFactory.CreateScope();
        var organizationDirectory = scope.ServiceProvider.GetRequiredService<IOrganizationDirectory>();
        var workOrderReportService = scope.ServiceProvider.GetRequiredService<WorkOrderReportService>();

        foreach (var organization in organizationDirectory.GetAll())
        {
            try
            {
                workOrderReportService.GetStatusReport(organization.Id);
            }
            catch (Exception ex)
            {
                // One organization's failure (e.g. a transient DB/Redis
                // blip) must not stop the whole warmer from ever running
                // again — the next tick, and every other organization this
                // tick, still needs to proceed.
                _logger.LogWarning(ex, "Failed to warm work order report cache for organization {OrganizationId}", organization.Id);
            }
        }
    }
}
