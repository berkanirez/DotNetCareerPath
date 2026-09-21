using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FieldOps.Modules.WorkOrders.Data;

internal class WorkOrdersDbContextFactory : IDesignTimeDbContextFactory<WorkOrdersDbContext>
{
    public WorkOrdersDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<WorkOrdersDbContext>();
        optionsBuilder.UseSqlServer("Server=localhost\\SQLEXPRESS;Database=FieldOpsWorkOrders;Trusted_Connection=True;TrustServerCertificate=True;");
        return new WorkOrdersDbContext(optionsBuilder.Options);
    }
}
