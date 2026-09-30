using FieldOps.NotificationService;
using FieldOps.NotificationService.Data;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

// Day 85: this service's half of distributed tracing — no ASP.NET Core
// instrumentation (this worker has no HTTP endpoints), just its own
// FieldOpsTracing spans (the "consume" side of RabbitMqEventPublisher's
// "publish" span in FieldOps.Api). "FieldOps.NotificationService" as the
// service name is what actually distinguishes this process's traces from
// FieldOps.Api's own in the shared console output.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("FieldOps.NotificationService"))
    .WithTracing(tracing => tracing
        .AddSource(FieldOpsTracing.MessagingSourceName)
        .AddConsoleExporter());

// Day 76: this service's ONLY database — its own connection string, never
// one of FieldOps.Api's five module connection strings. Per ADR 0005, this
// service has no way to even reach those databases.
var connectionString = builder.Configuration.GetConnectionString("FieldOpsNotificationsDb")
    ?? throw new InvalidOperationException("Missing connection string: FieldOpsNotificationsDb");
builder.Services.AddDbContext<NotificationServiceDbContext>(options => options.UseSqlServer(connectionString));

// Day 73's Inbox pattern, now backed by THIS service's own database instead
// of IWorkOrderDirectory — exactly the replacement ADR 0005 anticipated.
// Scoped, since NotificationServiceDbContext itself is Scoped; the consumer
// below resolves it through a fresh scope per message (Day 50's pattern).
builder.Services.AddScoped<IInboxStore, NotificationServiceInboxStore>();

builder.Services.AddSingleton<INotificationSender, LoggingNotificationSender>();

// WorkOrderCompletedEventConsumer reads "RabbitMq:HostName" itself via
// IConfiguration in its own constructor — no separate variable needed here.
builder.Services.AddHostedService<WorkOrderCompletedEventConsumer>();

var host = builder.Build();
host.Run();
