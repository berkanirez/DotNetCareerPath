namespace FieldOps.Modules.Employees;

// Deliberately does NOT validate that organizationId refers to a real
// Organization — this module has no way to check that (no reference to
// FieldOps.Modules.Organizations at all) and isn't meant to. That validation
// is the host's job (see FieldOps.Api's EmployeesController and ADR 0002),
// performed BEFORE Create is ever called.
public interface IEmployeeDirectory
{
    IReadOnlyList<EmployeeSummary> GetAll();
    EmployeeSummary? GetById(int id);
    EmployeeSummary Create(string name, int organizationId, EmployeeRole role);
}
