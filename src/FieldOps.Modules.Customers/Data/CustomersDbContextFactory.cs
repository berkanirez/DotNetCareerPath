using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FieldOps.Modules.Customers.Data;

internal class CustomersDbContextFactory : IDesignTimeDbContextFactory<CustomersDbContext>
{
    public CustomersDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CustomersDbContext>();
        optionsBuilder.UseSqlServer("Server=localhost\\SQLEXPRESS;Database=FieldOpsCustomers;Trusted_Connection=True;TrustServerCertificate=True;");
        return new CustomersDbContext(optionsBuilder.Options);
    }
}
