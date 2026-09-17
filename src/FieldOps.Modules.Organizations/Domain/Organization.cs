namespace FieldOps.Modules.Organizations.Domain;

// An Organization is a tenant — one of the separate companies subscribing to
// FieldOps. Every other module's data (Employees, Customers, Work Orders...)
// will eventually belong to exactly one Organization; tenant isolation
// (Week 8) builds directly on this.
internal class Organization
{
    public int Id { get; set; }
    public string Name { get; set; }

    public Organization(string name)
    {
        Name = name;
    }
}
