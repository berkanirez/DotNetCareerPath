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
            // Live-discovered bug (via a CI failure, not staged): the
            // per-organization try/catch below only protected
            // GetStatusReport itself — it never protected
            // GetRequiredService<WorkOrderReportService>() a few lines
            // above, which is exactly where IConnectionMultiplexer's lazy
            // Redis connection is actually attempted. When Redis was
            // unreachable (no Redis service in GitHub Actions' CI runner),
            // that threw OUTSIDE any try/catch, escaped ExecuteAsync
            // entirely, and — because BackgroundServiceExceptionBehavior
            // defaults to StopHost — took down the ENTIRE application host,
            // failing every unrelated test (and, in real production, every
            // unrelated request) sharing that same process. A single
            // background tick's failure must never be allowed to escape
            // ExecuteAsync at all, for any reason.
            try
            {
                WarmAllOrganizations();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Work order report cache warming tick failed");
            }
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
                // One organization's failure (e.g. a transient DB blip)
                // must not stop the rest of this same tick's organizations
                // from being warmed — the outer try/catch above is the
                // last-resort safety net; this inner one keeps failures
                // scoped as narrowly as possible when the failure really is
                // per-organization.
                _logger.LogWarning(ex, "Failed to warm work order report cache for organization {OrganizationId}", organization.Id);
            }
        }
    }
}
