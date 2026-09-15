using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockPilot.Api.Data;
using Testcontainers.MsSql;

namespace StockPilot.Api.Tests;

// A real, disposable SQL Server spun up in a Docker container just for this
// test run — replaces the app's real StockPilotDb registration below. The
// actual dev database (localhost\SQLEXPRESS) is never touched by these
// tests at all: nothing here depends on it, and nothing here pollutes it.
public class StockPilotApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _dbContainer =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    // Runs once, before any test in a class using this factory — starts the
    // container, then applies the SAME real EF Core migrations the actual
    // dev database uses (proving the schema is correct against a genuinely
    // fresh SQL Server, not just "whatever localhost\SQLEXPRESS already has").
    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        var options = new DbContextOptionsBuilder<StockPilotDbContext>()
            .UseSqlServer(_dbContainer.GetConnectionString())
            .Options;

        await using var context = new StockPilotDbContext(options);
        await context.Database.MigrateAsync();
        DbSeeder.Seed(context);
    }

    // Called when the test host actually builds (on first CreateClient()) —
    // swaps out the app's real StockPilotDbContext registration (pointed at
    // appsettings.Development.json's connection string) for one pointed at
    // this container instead.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var realDbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<StockPilotDbContext>));
            if (realDbContextDescriptor is not null)
            {
                services.Remove(realDbContextDescriptor);
            }

            services.AddDbContext<StockPilotDbContext>(options =>
                options.UseSqlServer(_dbContainer.GetConnectionString()));
        });
    }

    // Runs once, after every test in a class using this factory has finished —
    // tears the container down completely. Nothing survives to the next run.
    //
    // "new", not "override": WebApplicationFactory already has its own
    // DisposeAsync() from IAsyncDisposable, but it returns ValueTask, while
    // xUnit's IAsyncLifetime requires one that returns Task — two same-named
    // but incompatible signatures, so this one must be a separate method
    // (found and called by xUnit via IAsyncLifetime) rather than a genuine
    // override. It explicitly invokes the base's IAsyncDisposable.DisposeAsync
    // too, so the test host's own resources (HttpClient, TestServer) are
    // still cleaned up alongside our container.
    public new async Task DisposeAsync()
    {
        await _dbContainer.DisposeAsync();
        await ((IAsyncDisposable)this).DisposeAsync();
    }
}
