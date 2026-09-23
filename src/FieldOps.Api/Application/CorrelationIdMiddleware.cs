namespace FieldOps.Api.Application;

// Day 55: correlation ID — every request gets a "claim ticket" that every
// log line produced while handling it automatically carries, via
// ILogger.BeginScope. No controller or service code needs to change: this
// is transparent, request-scoped infrastructure, applied at the very start
// of the middleware pipeline so it wraps everything downstream.
public class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // A caller (e.g. another service, in a future Phase 4 world) may
        // already have its own correlation ID and want it carried through —
        // honor it if present. Otherwise, this is the first hop, so mint a
        // new one.
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing) && !string.IsNullOrWhiteSpace(existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString();

        context.Response.Headers[HeaderName] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }
}
