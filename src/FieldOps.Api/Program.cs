using FieldOps.Api.Application;
using FieldOps.Modules.AuditLogs;
using FieldOps.Modules.Customers;
using FieldOps.Modules.Employees;
using FieldOps.Modules.Organizations;
using FieldOps.Modules.WorkOrders;
using StackExchange.Redis;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// The host installs each module through its own extension method — it never
// names any module's internal concrete implementation class (or its
// DbContext) directly. Day 48/ADR 0003: every module now owns its own
// database; the host only ever hands over a connection string. The
// "get-connection-string-or-throw" logic is genuinely identical for all
// four calls (this week's "extract when it's really the same" rule), so it
// gets a tiny local function instead of being repeated four times.
string RequireConnectionString(string name) =>
    builder.Configuration.GetConnectionString(name) ?? throw new InvalidOperationException($"Missing connection string: {name}");

builder.Services.AddOrganizationsModule(RequireConnectionString("FieldOpsOrganizationsDb"));
builder.Services.AddEmployeesModule(RequireConnectionString("FieldOpsEmployeesDb"));
builder.Services.AddWorkOrdersModule(RequireConnectionString("FieldOpsWorkOrdersDb"));
builder.Services.AddCustomersModule(RequireConnectionString("FieldOpsCustomersDb"));
builder.Services.AddAuditLogsModule(RequireConnectionString("FieldOpsAuditLogsDb"));

// Application-layer service: cross-module orchestration that belongs to the
// host (Day 34), not inside either module or directly inside a controller.
builder.Services.AddScoped<EmployeeApplicationService>();
builder.Services.AddScoped<WorkOrderAssignmentService>();

// Day 48 (Redis): registered as a Singleton factory — the actual TCP
// connection to Redis is only made the first time something resolves
// IConnectionMultiplexer (WorkOrderReportService, only when the report
// endpoint is actually called), not eagerly at startup. Existing tests that
// never call that endpoint never touch Redis at all.
//
// Day 52 fix (live-discovered via a CI failure, then a slow local repro):
// AbortOnConnectFail defaults to true, meaning Connect() blocks — for as
// long as the underlying OS socket connect takes, which was observed to be
// far longer than StackExchange.Redis's own ConnectTimeout on this Windows
// machine — before throwing when Redis is unreachable. With it set to
// false, Connect() returns immediately regardless of Redis's availability;
// the multiplexer keeps retrying in the background, and any command issued
// while disconnected fails fast with a RedisConnectionException instead of
// blocking. Combined with WorkOrderReportCacheWarmer's own try/catch, a
// Redis outage now degrades quickly and gracefully instead of blocking or
// crashing the whole host.
var redisConnectionString = builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException("Missing configuration: Redis:ConnectionString");
var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
redisOptions.AbortOnConnectFail = false;
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));
builder.Services.AddScoped<WorkOrderReportService>();
builder.Services.AddHostedService<WorkOrderReportCacheWarmer>();

// Day 51: notification abstraction — WorkOrdersController only ever depends
// on INotificationSender, never on this concrete demo implementation. A
// real provider (email/SMS/push) would later replace this registration
// alone, with zero controller changes.
builder.Services.AddSingleton<INotificationSender, LoggingNotificationSender>();

// Day 53: per-organization rate limiting — resource/performance isolation,
// the natural counterpart to Week 8's data isolation (a tenant can't see
// another tenant's data; now, a tenant can't degrade another tenant's
// performance either). Partitioned by X-Organization-Id, not by IP/user, so
// each organization gets its own independent "bucket" regardless of how
// many employees within it are making requests. A missing header falls
// into one shared "unknown" bucket — deliberately: a request with no
// organization identity is rejected by ValidateMembership anyway (400),
// long before it would ever reach a real database write, so a shared,
// generous bucket for that case is enough. This is .NET's own built-in
// middleware (Microsoft.AspNetCore.RateLimiting, since .NET 7) — no
// third-party package needed, the same idiomatic-native-first spirit as
// Day 50's BackgroundService over Hangfire/Quartz.
//
// PermitLimit/Window read from configuration, not hardcoded: the real demo
// value (small, so a human can trigger 429 in a few seconds) would break
// existing integration tests, which legitimately create far more than 5
// work orders for the same organization within far less than 10 seconds.
// FieldOpsApiFactory overrides these two settings to a much higher limit
// for the test environment — the same "override a config VALUE, not the
// business code" pattern Day 48 established for connection strings.
var rateLimitPermitLimit = builder.Configuration.GetValue("RateLimiting:PerOrganization:PermitLimit", 5);
var rateLimitWindowSeconds = builder.Configuration.GetValue("RateLimiting:PerOrganization:WindowSeconds", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("PerOrganization", httpContext =>
    {
        var organizationId = httpContext.Request.Headers["X-Organization-Id"].FirstOrDefault() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(organizationId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitPermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitWindowSeconds),
            QueueLimit = 0
        });
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseRateLimiter();

app.UseAuthorization();

app.MapControllers();

app.Run();
