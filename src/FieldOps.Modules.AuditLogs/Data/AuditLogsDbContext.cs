using FieldOps.Modules.AuditLogs.Domain;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Modules.AuditLogs.Data;

// internal — same ADR 0003 pattern as every other module's DbContext.
// No seed data: an audit log starts genuinely empty, unlike Organizations/
// Employees/Customers, which needed fixed seeded Ids other modules already
// assumed.
internal class AuditLogsDbContext : DbContext
{
    public AuditLogsDbContext(DbContextOptions<AuditLogsDbContext> options) : base(options)
    {
    }

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLogEntry>(entity =>
        {
            entity.Property(e => e.Action).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ActorType).IsRequired().HasMaxLength(50);
        });
    }
}
