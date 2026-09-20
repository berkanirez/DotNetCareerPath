using FieldOps.Modules.Customers.Domain;

namespace FieldOps.Modules.Customers;

// internal, not public — the host must never be able to name this concrete
// class directly (only ICustomerDirectory), the same enforced boundary as
// every other module's InMemory* implementation (Day 32/33/40).
// Seeded like InMemoryOrganizationDirectory (Day 32): one customer per
// organization (Id 1/2, matching Organizations' own seeded Ids), so
// approval scenarios are demonstrable immediately without a Create
// endpoint — no Customer-creation flow exists yet, deliberately out of
// today's scope.
internal class InMemoryCustomerDirectory : ICustomerDirectory
{
    private readonly List<Customer> _customers = new()
    {
        new Customer("Acme Field Services' Customer", organizationId: 1) { Id = 1 },
        new Customer("Blue Ridge Maintenance's Customer", organizationId: 2) { Id = 2 }
    };

    public IReadOnlyList<CustomerSummary> GetAll()
    {
        return _customers.Select(ToSummary).ToList();
    }

    public CustomerSummary? GetById(int id)
    {
        var customer = _customers.FirstOrDefault(c => c.Id == id);
        return customer is null ? null : ToSummary(customer);
    }

    private static CustomerSummary ToSummary(Customer customer) =>
        new(customer.Id, customer.Name, customer.OrganizationId);
}
