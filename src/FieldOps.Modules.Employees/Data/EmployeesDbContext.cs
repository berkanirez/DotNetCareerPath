using FieldOps.Modules.Employees.Domain;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Modules.Employees.Data;

// internal — same ADR 0003 pattern as OrganizationsDbContext (Day 48):
// this module owns its own database, the host never touches this type.
internal class EmployeesDbContext : DbContext
{
    public EmployeesDbContext(DbContextOptions<EmployeesDbContext> options) : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);

            // Same five seeded employees, same Ids, as InMemoryEmployeeDirectory
            // (Day 37) — WorkOrders' tests still assume Employee Ids 1-5 mean
            // exactly what they meant before.
            entity.HasData(
                new Employee("Org1 Admin", organizationId: 1, EmployeeRole.Admin) { Id = 1 },
                new Employee("Org1 Member", organizationId: 1, EmployeeRole.Member) { Id = 2 },
                new Employee("Org2 Admin", organizationId: 2, EmployeeRole.Admin) { Id = 3 },
                new Employee("Org2 Member", organizationId: 2, EmployeeRole.Member) { Id = 4 },
                new Employee("Orphaned Admin", organizationId: 999, EmployeeRole.Admin) { Id = 5 }
            );
        });
    }
}
