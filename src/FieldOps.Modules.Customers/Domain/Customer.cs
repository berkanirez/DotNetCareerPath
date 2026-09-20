namespace FieldOps.Modules.Customers.Domain;

// OrganizationId is a plain int — same reasoning as every other module's
// OrganizationId (Day 33, ADR 0002): no project reference to
// FieldOps.Modules.Organizations exists or is needed here.
internal class Customer
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int OrganizationId { get; set; }

    public Customer(string name, int organizationId)
    {
        Name = name;
        OrganizationId = organizationId;
    }
}
