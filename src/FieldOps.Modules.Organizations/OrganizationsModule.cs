using FieldOps.Modules.Organizations.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.Modules.Organizations;

// The module's own "installation" entry point — the host calls this ONE
// method and never needs to name EfOrganizationDirectory (or
// OrganizationsDbContext) itself. This is what makes those classes safe to
// keep `internal`: nothing outside this project ever needs to spell their
// names. Day 48: the host now passes a connection string, but never touches
// EF Core directly — configuring OrganizationsDbContext is entirely this
// module's own business (ADR 0003).
public static class OrganizationsModule
{
    public static IServiceCollection AddOrganizationsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<OrganizationsDbContext>(options => options.UseSqlServer(connectionString));
        return services.AddScoped<IOrganizationDirectory, EfOrganizationDirectory>();
    }
}
