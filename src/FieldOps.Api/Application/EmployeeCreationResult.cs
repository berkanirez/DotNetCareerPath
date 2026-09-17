using FieldOps.Modules.Employees;

namespace FieldOps.Api.Application;

// Deliberately not an ASP.NET Core type (no ActionResult, no status codes
// here) — this is a plain outcome that EmployeeApplicationService can
// produce without knowing it will ever be turned into an HTTP response.
// EmployeesController is the only thing that knows HTTP exists.
public class EmployeeCreationResult
{
    public bool Succeeded { get; }
    public EmployeeSummary? Employee { get; }
    public string? Error { get; }

    private EmployeeCreationResult(bool succeeded, EmployeeSummary? employee, string? error)
    {
        Succeeded = succeeded;
        Employee = employee;
        Error = error;
    }

    public static EmployeeCreationResult Success(EmployeeSummary employee) =>
        new(succeeded: true, employee, error: null);

    public static EmployeeCreationResult Failure(string error) =>
        new(succeeded: false, employee: null, error);
}
