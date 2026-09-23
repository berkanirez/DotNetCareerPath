using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace FieldOps.Api.Application;

// Day 56: a readiness check — "can this instance actually reach Redis right
// now," not "is the app process alive" (that's /health/live, which touches
// no dependency at all). IHealthCheck is a single-method interface built
// into the framework (Microsoft.Extensions.Diagnostics.HealthChecks), no
// extra NuGet package needed for this custom implementation.
public class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public RedisHealthCheck(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy("Redis is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis is not reachable.", ex);
        }
    }
}
