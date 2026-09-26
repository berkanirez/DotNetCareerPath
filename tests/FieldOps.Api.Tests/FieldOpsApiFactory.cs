using FieldOps.Api.Application;
using FieldOps.Modules.AuditLogs.Data;
using FieldOps.Modules.Customers.Data;
using FieldOps.Modules.Employees.Data;
using FieldOps.Modules.Organizations.Data;
using FieldOps.Modules.WorkOrders.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace FieldOps.Api.Tests;

// Day 48: mirrors StockPilot's StockPilotApiFactory (Day 28) — a real,
// disposable SQL Server spun up in a Docker container just for this test
// run. The real dev databases (localhost\SQLEXPRESS) are never touched by
// these tests at all. One container hosts all five modules' databases
// (distinct names, same instance) — the same "same instance, separate
// databases" shape ADR 0003 already uses for local dev, just disposable.
// No DbSeeder.Seed calls needed — each DbContext's own HasData (baked into
// its migration) seeds itself automatically when MigrateAsync runs.
public class FieldOpsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _dbContainer =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        await MigrateAsync(new DbContextOptionsBuilder<OrganizationsDbContext>()
            .UseSqlServer(ConnectionStringFor("FieldOpsOrganizations")).Options, o => new OrganizationsDbContext(o));
        await MigrateAsync(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseSqlServer(ConnectionStringFor("FieldOpsEmployees")).Options, o => new EmployeesDbContext(o));
        await MigrateAsync(new DbContextOptionsBuilder<WorkOrdersDbContext>()
            .UseSqlServer(ConnectionStringFor("FieldOpsWorkOrders")).Options, o => new WorkOrdersDbContext(o));
        await MigrateAsync(new DbContextOptionsBuilder<CustomersDbContext>()
            .UseSqlServer(ConnectionStringFor("FieldOpsCustomers")).Options, o => new CustomersDbContext(o));
        await MigrateAsync(new DbContextOptionsBuilder<AuditLogsDbContext>()
            .UseSqlServer(ConnectionStringFor("FieldOpsAuditLogs")).Options, o => new AuditLogsDbContext(o));
    }

    private static async Task MigrateAsync<TContext, TOptions>(TOptions options, Func<TOptions, TContext> create)
        where TContext : DbContext
    {
        await using var context = create(options);
        await context.Database.MigrateAsync();
    }

    // Same database name as the container's own default catalog, just
    // swapped out per module — SqlConnectionStringBuilder is the clean way
    // to change only the database name without hand-editing a raw string.
    private string ConnectionStringFor(string database)
    {
        var builder = new SqlConnectionStringBuilder(_dbContainer.GetConnectionString())
        {
            InitialCatalog = database
        };
        return builder.ConnectionString;
    }

    // Overrides the connection string CONFIGURATION VALUES that each
    // Add*Module call reads at startup — never touches any module's
    // DbContext or Ef*Directory by name here, for any of the five modules.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:FieldOpsOrganizationsDb", ConnectionStringFor("FieldOpsOrganizations"));
        builder.UseSetting("ConnectionStrings:FieldOpsEmployeesDb", ConnectionStringFor("FieldOpsEmployees"));
        builder.UseSetting("ConnectionStrings:FieldOpsWorkOrdersDb", ConnectionStringFor("FieldOpsWorkOrders"));
        builder.UseSetting("ConnectionStrings:FieldOpsCustomersDb", ConnectionStringFor("FieldOpsCustomers"));
        builder.UseSetting("ConnectionStrings:FieldOpsAuditLogsDb", ConnectionStringFor("FieldOpsAuditLogs"));

        // Day 53: the real, demo-sized rate limit (5 requests / 10 seconds
        // per organization) is far too strict for these tests — many
        // legitimately create more than 5 work orders for the same
        // organization within a single test class's run, which shares one
        // in-memory limiter instance for its whole lifetime. Overridden to
        // an effectively-unlimited value here so these authorization/
        // business-logic tests never fail for an unrelated reason; the
        // actual 429 threshold is verified live, not by an automated test.
        builder.UseSetting("RateLimiting:PerOrganization:PermitLimit", "100000");

        // Day 67: real, live-caught test-suite slowdown — every test calling
        // Complete now tries to open a genuine RabbitMQ connection, which
        // has no chance of succeeding here (no RabbitMQ container in this
        // test environment) and only fails after its own connection
        // timeout. The full suite's run time roughly doubled before this
        // override was added. Same fix shape as Day 53's rate-limiting
        // override: swap out the real, slow, externally-dependent
        // implementation for a fast no-op, using the exact
        // ConfigureServices-runs-after-Program.cs mechanism Day 64 already
        // proved (there, to inject a failing fake; here, to inject a
        // free one).
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IEventPublisher, NoOpEventPublisher>();
        });
    }

    // Deliberately does nothing and never fails — these tests care about
    // WorkOrdersController's own behavior, not about proving RabbitMQ
    // connectivity (Day 66/67 already proved that live, separately).
    private class NoOpEventPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // "new", not "override" — same reason as StockPilot's version (Day 28):
    // WebApplicationFactory's own IAsyncDisposable.DisposeAsync() returns
    // ValueTask, while xUnit's IAsyncLifetime requires one returning Task.
    public new async Task DisposeAsync()
    {
        await _dbContainer.DisposeAsync();
        await ((IAsyncDisposable)this).DisposeAsync();
    }
}
