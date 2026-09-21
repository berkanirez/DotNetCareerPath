using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FieldOps.Modules.Organizations.Data;

// Needed only because this DbContext lives in a class library, not a
// startup project with its own Program.cs to build a service provider from
// — `dotnet ef` has no other way to construct OrganizationsDbContext at
// design time (to create/apply migrations). The connection string here is
// only used by the CLI tooling itself, never by the running application
// (Program.cs supplies the real one via AddOrganizationsModule).
internal class OrganizationsDbContextFactory : IDesignTimeDbContextFactory<OrganizationsDbContext>
{
    public OrganizationsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OrganizationsDbContext>();
        optionsBuilder.UseSqlServer("Server=localhost\\SQLEXPRESS;Database=FieldOpsOrganizations;Trusted_Connection=True;TrustServerCertificate=True;");
        return new OrganizationsDbContext(optionsBuilder.Options);
    }
}
