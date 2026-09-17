using FieldOps.Modules.Employees.Domain;

namespace FieldOps.Modules.Employees;

internal class InMemoryEmployeeDirectory : IEmployeeDirectory
{
    private readonly List<Employee> _employees = new();
    private int _nextId = 1;

    public IReadOnlyList<EmployeeSummary> GetAll()
    {
        return _employees.Select(ToSummary).ToList();
    }

    public EmployeeSummary Create(string name, int organizationId)
    {
        var employee = new Employee(name, organizationId) { Id = _nextId++ };
        _employees.Add(employee);
        return ToSummary(employee);
    }

    private static EmployeeSummary ToSummary(Employee employee) =>
        new(employee.Id, employee.Name, employee.OrganizationId);
}
