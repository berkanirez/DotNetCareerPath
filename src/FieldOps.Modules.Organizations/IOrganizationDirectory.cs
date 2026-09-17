namespace FieldOps.Modules.Organizations;

// The module's public front door — the only type the host (FieldOps.Api) or
// any other module is allowed to depend on. Everything inside Domain/ is
// `internal` and genuinely unreachable from outside this project (enforced
// by the compiler, not just by convention) — this interface, returning only
// OrganizationSummary, is the entire contract.
public interface IOrganizationDirectory
{
    IReadOnlyList<OrganizationSummary> GetAll();

    OrganizationSummary? GetById(int id);
}
