using FieldOps.Modules.Organizations.Domain;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Modules.Organizations.Data;

// internal, replacing InMemoryOrganizationDirectory (Day 32) as
// IOrganizationDirectory's real implementation — the interface itself, and
// every caller through it (OrganizationsController, the whole rest of the
// codebase), didn't need to change at all. Deliberately still synchronous
// today, mirroring StockPilot's own Day 16 (persistence first) before
// Day 17's separate async-conversion day — not conflating two new concepts
// in one sitting.
internal class EfOrganizationDirectory : IOrganizationDirectory
{
    private readonly OrganizationsDbContext _dbContext;

    public EfOrganizationDirectory(OrganizationsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IReadOnlyList<OrganizationSummary> GetAll()
    {
        return _dbContext.Organizations
            .Select(o => new OrganizationSummary(o.Id, o.Name))
            .ToList();
    }

    public OrganizationSummary? GetById(int id)
    {
        var organization = _dbContext.Organizations.FirstOrDefault(o => o.Id == id);
        return organization is null ? null : new OrganizationSummary(organization.Id, organization.Name);
    }
}
