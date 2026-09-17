using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.Modules.Organizations;

// The module's own "installation" entry point — the host calls this ONE
// method and never needs to name InMemoryOrganizationDirectory (or, later,
// an EF Core-backed replacement) itself. This is what makes that class safe
// to keep `internal`: nothing outside this project ever needs to spell its name.
public static class OrganizationsModule
{
    public static IServiceCollection AddOrganizationsModule(this IServiceCollection services)
    {
        return services.AddSingleton<IOrganizationDirectory, InMemoryOrganizationDirectory>();
    }
}
