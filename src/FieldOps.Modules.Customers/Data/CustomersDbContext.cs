using FieldOps.Modules.Customers.Domain;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Modules.Customers.Data;

// internal — same ADR 0003 pattern as OrganizationsDbContext (Day 48).
internal class CustomersDbContext : DbContext
{
    public CustomersDbContext(DbContextOptions<CustomersDbContext> options) : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.Property(c => c.Name).IsRequired().HasMaxLength(200);

            // Same two seeded customers, same Ids, as InMemoryCustomerDirectory (Day 47).
            entity.HasData(
                new Customer("Acme Field Services' Customer", organizationId: 1) { Id = 1 },
                new Customer("Blue Ridge Maintenance's Customer", organizationId: 2) { Id = 2 }
            );
        });
    }
}
