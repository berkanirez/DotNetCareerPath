using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FieldOps.Modules.AuditLogs.Data;

internal class AuditLogsDbContextFactory : IDesignTimeDbContextFactory<AuditLogsDbContext>
{
    public AuditLogsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AuditLogsDbContext>();
        optionsBuilder.UseSqlServer("Server=localhost\\SQLEXPRESS;Database=FieldOpsAuditLogs;Trusted_Connection=True;TrustServerCertificate=True;");
        return new AuditLogsDbContext(optionsBuilder.Options);
    }
}
