using Microsoft.EntityFrameworkCore;

namespace FieldOps.NotificationService.Data;

// Day 76: this service's ENTIRE database — only Inbox/dead-letter
// bookkeeping, per ADR 0005. No WorkOrder, Employee, Organization or
// Customer table exists here, and never will; this service only ever
// learns what WorkOrderCompletedEvent itself carries.
internal class NotificationServiceDbContext : DbContext
{
    public NotificationServiceDbContext(DbContextOptions<NotificationServiceDbContext> options) : base(options)
    {
    }

    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<FailedMessageAttempt> FailedMessageAttempts => Set<FailedMessageAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessedMessage>(entity =>
        {
            entity.Property(m => m.ConsumerName).IsRequired().HasMaxLength(200);
            entity.Property(m => m.MessageId).IsRequired().HasMaxLength(200);
            entity.HasIndex(m => new { m.ConsumerName, m.MessageId }).IsUnique();
        });

        modelBuilder.Entity<FailedMessageAttempt>(entity =>
        {
            entity.Property(m => m.ConsumerName).IsRequired().HasMaxLength(200);
            entity.Property(m => m.MessageId).IsRequired().HasMaxLength(200);
            entity.HasIndex(m => new { m.ConsumerName, m.MessageId }).IsUnique();
        });
    }
}
