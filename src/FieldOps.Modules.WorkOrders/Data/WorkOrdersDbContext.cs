using FieldOps.Modules.WorkOrders.Domain;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Modules.WorkOrders.Data;

// internal — same ADR 0003 pattern as OrganizationsDbContext (Day 48).
internal class WorkOrdersDbContext : DbContext
{
    public WorkOrdersDbContext(DbContextOptions<WorkOrdersDbContext> options) : base(options)
    {
    }

    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();

    // Day 71: the Outbox pattern's table, in the same database/DbContext as
    // WorkOrder itself — this is what makes a status change and its outbox
    // row commit atomically in one SaveChanges/transaction. A separate
    // database (even one for "all outbox messages") would defeat the whole
    // point: the guarantee only exists because both writes go through the
    // exact same DbContext instance's change tracker.
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkOrder>(entity =>
        {
            entity.Property(w => w.Title).IsRequired().HasMaxLength(200);

            // EF Core's "primitive collection" support (EF Core 8+) maps
            // List<string> to a JSON column on SQL Server automatically —
            // no separate EvidenceNote table needed for a demo-simplified,
            // text-only stand-in for real file evidence (Day 46).
            entity.PrimitiveCollection(w => w.EvidenceNotes);

            // No HasData — WorkOrders started empty in-memory (Day 40, no
            // bootstrap problem the way Employees had) and stays empty here.
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.Property(m => m.EventType).IsRequired().HasMaxLength(200);
            entity.Property(m => m.Payload).IsRequired();
        });
    }
}
