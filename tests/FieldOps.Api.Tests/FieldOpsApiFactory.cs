using FieldOps.Modules.AuditLogs.Data;
using FieldOps.Modules.Customers.Data;
using FieldOps.Modules.Employees.Data;
using FieldOps.Modules.Organizations.Data;
using FieldOps.Modules.WorkOrders.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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
