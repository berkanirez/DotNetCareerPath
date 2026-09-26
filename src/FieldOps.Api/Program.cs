using FieldOps.Api.Application;
using FieldOps.Modules.AuditLogs;
using FieldOps.Modules.Customers;
using FieldOps.Modules.Employees;
using FieldOps.Modules.Organizations;
using FieldOps.Modules.WorkOrders;
using StackExchange.Redis;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Day 55: structured logging — JSON console output, with scopes turned on
// so CorrelationIdMiddleware's per-request scope actually appears in the
// output (IncludeScopes defaults to false; without it, BeginScope silently
// does nothing visible). JSON (not the plain-text simple console formatter)
// because it's the one built-in formatter that serializes a Dictionary-based
// scope into real, separate fields rather than just calling ToString() on it.
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
});

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

// Day 54: idempotency — no per-request state of its own (just IConnectionMultiplexer,
// itself a Singleton), so this can safely be a Singleton too.
builder.Services.AddSingleton<IdempotencyService>();

// Day 51: notification abstraction — WorkOrdersController only ever depends
// on INotificationSender, never on this concrete demo implementation. A
// real provider (email/SMS/push) would later replace this registration
// alone, with zero controller changes.
builder.Services.AddSingleton<INotificationSender, LoggingNotificationSender>();

// Day 63: AI provider abstraction — the same shape as Day 51's
// INotificationSender above. WorkOrderNoteSummaryService only ever depends
// on IAiProvider; a real provider (OpenAI/Anthropic) would later replace
// this one registration, with zero changes to the controller or the
// summary service itself. Both are Singletons: FakeAiProvider holds no
// state, and WorkOrderNoteSummaryService's only dependency is that same
// stateless Singleton.
builder.Services.AddSingleton<IAiProvider, FakeAiProvider>();
builder.Services.AddSingleton<WorkOrderNoteSummaryService>();

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

// Day 56: health checks — liveness ("is the process even running," no
// dependency touched, cheap and instant) vs. readiness ("can this instance
// actually do its job right now," which means its critical dependencies
// must be reachable). Both custom IHealthCheck implementations take only a
// connection string / the existing IConnectionMultiplexer — never a
// module's internal DbContext, preserving ADR 0001/0002's boundary.
// Only one of the five SQL Server databases (WorkOrders) is checked today —
// a deliberate scope narrowing, not an oversight; they all live on the same
// physical SQL Server instance in this demo setup.
builder.Services.AddHealthChecks()
    .AddCheck<RedisHealthCheck>("redis", tags: ["ready"])
    .AddTypeActivatedCheck<SqlServerHealthCheck>(
        "workorders-db",
        failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
        tags: ["ready"],
        args: [RequireConnectionString("FieldOpsWorkOrdersDb")]);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Day 55: registered before everything else so the correlation ID scope
// wraps the entire rest of the pipeline — every log line produced by any
// later middleware, controller, or service during this request inherits it.
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseHttpsRedirection();

app.UseRateLimiter();

app.UseAuthorization();

// Day 58: the framework's default health check response is just the bare
// word "Healthy"/"Unhealthy" — no way to tell WHICH check failed. A JSON
// breakdown per check is what made today's Docker networking diagnosis
// (which dependency is actually unreachable from inside the container)
// observable at all, rather than a single opaque failure.
static Task WriteHealthCheckResponse(HttpContext context, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
{
    context.Response.ContentType = "application/json";
    var payload = System.Text.Json.JsonSerializer.Serialize(new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description
        })
    });
    return context.Response.WriteAsync(payload);
}

// Day 56: /health/live never runs any check (Predicate: _ => false) — pure
// "is the process responding at all." /health/ready runs only the checks
// tagged "ready" — the real dependency checks.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthCheckResponse
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthCheckResponse
});

app.MapControllers();

// Day 66: TEMP demonstration block — proves a message genuinely travels
// FieldOps.Api -> RabbitMQ -> back to FieldOps.Api, before any real domain
// event is wired up. Development-only and deliberately temporary (the same
// role RoadmapOS Day 2's console verification block played): today's goal
// is "does the mechanism work at all," not a production feature. Removed
// once a real producer/consumer replaces it.
if (app.Environment.IsDevelopment())
{
    var rabbitMqHostName = builder.Configuration["RabbitMq:HostName"] ?? "localhost";
    var factory = new RabbitMQ.Client.ConnectionFactory { HostName = rabbitMqHostName };

    try
    {
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        const string queueName = "fieldops.day66.demo";
        await channel.QueueDeclareAsync(queue: queueName, durable: false, exclusive: false, autoDelete: false);

        var consumer = new RabbitMQ.Client.Events.AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var received = System.Text.Encoding.UTF8.GetString(ea.Body.ToArray());
            app.Logger.LogInformation("Day 66 RabbitMQ demo: consumer received message: {Message}", received);
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        };
        await channel.BasicConsumeAsync(
            queue: queueName,
            autoAck: false,
            consumerTag: string.Empty,
            noLocal: false,
            exclusive: false,
            arguments: null,
            consumer: consumer);

        var message = $"Hello from FieldOps.Api, {DateTime.UtcNow:O}";
        var body = System.Text.Encoding.UTF8.GetBytes(message);
        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queueName,
            mandatory: false,
            basicProperties: new RabbitMQ.Client.BasicProperties(),
            body: (ReadOnlyMemory<byte>)body);
        app.Logger.LogInformation("Day 66 RabbitMQ demo: published message to queue '{Queue}'", queueName);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Day 66 RabbitMQ demo skipped: RabbitMQ is not reachable at '{HostName}'", rabbitMqHostName);
    }
}

app.Run();
