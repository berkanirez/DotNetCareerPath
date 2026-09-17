using FieldOps.Modules.Organizations.Domain;

namespace FieldOps.Modules.Organizations;

// internal, not public: the host must never be able to name this concrete
// class directly (only IOrganizationDirectory). See OrganizationsModule.cs
// for how the host registers it without ever seeing this type name.
//
// In-memory only — no persistence yet, the same deliberate first-step
// RoadmapOS took with InMemorySkillCatalog (Day 3) and StockPilot with
// InMemoryProductStore (Day 14): prove the module boundary and the vertical
// slice work before EF Core enters the picture.
internal class InMemoryOrganizationDirectory : IOrganizationDirectory
{
    private readonly List<Organization> _organizations = new()
    {
        new Organization("Acme Field Services") { Id = 1 },
        new Organization("Blue Ridge Maintenance") { Id = 2 }
    };

    public IReadOnlyList<OrganizationSummary> GetAll()
    {
        return _organizations.Select(o => new OrganizationSummary(o.Id, o.Name)).ToList();
    }

    public OrganizationSummary? GetById(int id)
    {
        var organization = _organizations.FirstOrDefault(o => o.Id == id);
        if (organization == null)
        {
            return null;
        }

        return new OrganizationSummary(organization.Id, organization.Name);
    }
}
