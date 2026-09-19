namespace FieldOps.Modules.Employees;

// Membership: an employee's role within its own organization — not a
// system-wide role like StockPilot's Admin/Employee (Day 25). Two employees
// with the same EmployeeRole in two different organizations have no
// relationship to each other at all; the role only ever means something
// scoped to that one OrganizationId.
public enum EmployeeRole
{
    Admin,
    Member
}
