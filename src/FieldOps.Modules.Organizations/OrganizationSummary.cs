namespace FieldOps.Modules.Organizations;

// The module's public shape for an Organization — deliberately separate from
// the internal Domain.Organization entity. FieldOps.Api (or any future
// module) is only ever allowed to see this, never the internal entity itself.
public record OrganizationSummary(int Id, string Name);
