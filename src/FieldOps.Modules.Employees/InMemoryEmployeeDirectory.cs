using FieldOps.Modules.Employees.Domain;

namespace FieldOps.Modules.Employees;

internal class InMemoryEmployeeDirectory : IEmployeeDirectory
{
    // Seeded so RBAC has someone to start from: with no employees at all,
    // an "only Admins can create employees" rule would lock every
    // organization out of ever having a first employee. One Admin + one
    // Member per organization (matching InMemoryOrganizationDirectory's
    // seeded Id 1/2) makes both the allowed and denied paths demonstrable
    // immediately.
    private readonly List<Employee> _employees = new()
    {
        new Employee("Org1 Admin", organizationId: 1, EmployeeRole.Admin) { Id = 1 },
        new Employee("Org1 Member", organizationId: 1, EmployeeRole.Member) { Id = 2 },
        new Employee("Org2 Admin", organizationId: 2, EmployeeRole.Admin) { Id = 3 },
        new Employee("Org2 Member", organizationId: 2, EmployeeRole.Member) { Id = 4 }
    };
    private int _nextId = 5;

    public IReadOnlyList<EmployeeSummary> GetAll()
    {
        return _employees.Select(ToSummary).ToList();
    }

    public EmployeeSummary? GetById(int id)
    {
        var employee = _employees.FirstOrDefault(e => e.Id == id);
        return employee is null ? null : ToSummary(employee);
    }

    public EmployeeSummary Create(string name, int organizationId, EmployeeRole role)
    {
        var employee = new Employee(name, organizationId, role) { Id = _nextId++ };
        _employees.Add(employee);
        return ToSummary(employee);
    }

    private static EmployeeSummary ToSummary(Employee employee) =>
        new(employee.Id, employee.Name, employee.OrganizationId, employee.Role);
}
