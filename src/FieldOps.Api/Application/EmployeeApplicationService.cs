using FieldOps.Modules.Employees;
using FieldOps.Modules.Organizations;

namespace FieldOps.Api.Application;

// The cross-module orchestration that used to live directly inside
// EmployeesController.Create (Day 33) — moved here so it has exactly one
// job (decide whether an employee CAN be created, and create it if so) and
// one reason to change (the business rule itself), separate from
// EmployeesController's own reason to change (the HTTP shape of the
// request/response). This class has no ASP.NET Core dependency at all,
// which is what makes it testable with a plain xUnit test — no
// ControllerBase, no ActionResult, no HTTP involved.
public class EmployeeApplicationService
{
    private readonly IEmployeeDirectory _employeeDirectory;
    private readonly IOrganizationDirectory _organizationDirectory;

    public EmployeeApplicationService(IEmployeeDirectory employeeDirectory, IOrganizationDirectory organizationDirectory)
    {
        _employeeDirectory = employeeDirectory;
        _organizationDirectory = organizationDirectory;
    }

    public EmployeeCreationResult CreateEmployee(string name, int organizationId)
    {
        var organization = _organizationDirectory.GetById(organizationId);
        if (organization is null)
        {
            return EmployeeCreationResult.Failure($"Organization {organizationId} does not exist.");
        }

        // Every employee created through this API starts as a Member —
        // creating a new Admin isn't supported yet (out of Day 37's scope).
        // Today's seeded Admins (InMemoryEmployeeDirectory) are the only
        // Admins that exist.
        var employee = _employeeDirectory.Create(name, organizationId, EmployeeRole.Member);
        return EmployeeCreationResult.Success(employee);
    }
}
