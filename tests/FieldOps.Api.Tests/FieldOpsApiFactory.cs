using FieldOps.Modules.Organizations.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace FieldOps.Api.Tests;

// Day 48: mirrors StockPilot's StockPilotApiFactory (Day 28) almost exactly —
// a real, disposable SQL Server spun up in a Docker container just for this
// test run. The real dev database (localhost\SQLEXPRESS, appsettings.
// Development.json's FieldOpsOrganizationsDb) is never touched by these
// tests at all. Unlike StockPilot, there's no DbSeeder.Seed call needed —
// OrganizationsDbContext's own HasData (in the migration itself) already
// seeds Org 1/2 automatically when MigrateAsync runs.
public class FieldOpsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _dbContainer =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        var options = new DbContextOptionsBuilder<OrganizationsDbContext>()
            .UseSqlServer(_dbContainer.GetConnectionString())
            .Options;

        await using var context = new OrganizationsDbContext(options);
        await context.Database.MigrateAsync();
    }

    // Overrides the connection string CONFIGURATION VALUE that
    // AddOrganizationsModule reads at startup — never touches
    // OrganizationsDbContext or EfOrganizationDirectory by name here. This
    // is a stricter boundary than StockPilot's version (which swaps the
    // DbContext service registration directly, since StockPilotDbContext is
    // public): FieldOps.Api itself still never needs to know either type
    // exists, exactly like production code never does.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:FieldOpsOrganizationsDb", _dbContainer.GetConnectionString());
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
