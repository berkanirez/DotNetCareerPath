using Microsoft.EntityFrameworkCore;
using RoadmapOS.Web.Domain;

namespace RoadmapOS.Web.Data;

public class RoadmapOSDbContext : DbContext
{
    public RoadmapOSDbContext(DbContextOptions<RoadmapOSDbContext> options) : base(options)
    {
    }

    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<RoadmapPhase> RoadmapPhases => Set<RoadmapPhase>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Milestone> Milestones => Set<Milestone>();
    public DbSet<Evidence> EvidenceRecords => Set<Evidence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Skill>(entity =>
        {
            entity.Property(s => s.Name).HasMaxLength(100);
            entity.Property(s => s.Category).HasMaxLength(100);
            entity.Property(s => s.Notes).HasMaxLength(500);
        });

        modelBuilder.Entity<RoadmapPhase>(entity =>
        {
            entity.Property(p => p.Name).IsRequired().HasMaxLength(150);
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.Property(p => p.Name).IsRequired().HasMaxLength(150);

            entity.HasOne(p => p.RoadmapPhase)
                .WithMany(ph => ph.Projects)
                .HasForeignKey(p => p.RoadmapPhaseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Milestone>(entity =>
        {
            entity.Property(m => m.Name).IsRequired().HasMaxLength(150);

            entity.HasOne(m => m.Project)
                .WithMany(p => p.Milestones)
                .HasForeignKey(m => m.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Evidence>(entity =>
        {
            entity.Property(e => e.Description).IsRequired().HasMaxLength(300);

            entity.HasOne(e => e.Skill)
                .WithMany(s => s.EvidenceRecords)
                .HasForeignKey(e => e.SkillId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
