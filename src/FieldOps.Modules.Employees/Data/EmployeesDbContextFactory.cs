using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FieldOps.Modules.Employees.Data;

// Design-time factory — same reason as OrganizationsDbContextFactory
// (Day 48): a class library has no Program.cs for `dotnet ef` to build a
// service provider from.
internal class EmployeesDbContextFactory : IDesignTimeDbContextFactory<EmployeesDbContext>
{
    public EmployeesDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EmployeesDbContext>();
        optionsBuilder.UseSqlServer("Server=localhost\\SQLEXPRESS;Database=FieldOpsEmployees;Trusted_Connection=True;TrustServerCertificate=True;");
        return new EmployeesDbContext(optionsBuilder.Options);
    }
}
