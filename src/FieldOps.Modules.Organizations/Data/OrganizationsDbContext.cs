using FieldOps.Modules.Organizations.Domain;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Modules.Organizations.Data;

// internal, like every other type this module doesn't expose through
// IOrganizationDirectory — the host never touches EF Core here directly,
// not even to pass a connection string in a special way (AddOrganizationsModule
// still takes a plain string). ADR 0003: this module owns its own database,
// separate from every other module's — no cross-module joins are possible
// even by accident.
internal class OrganizationsDbContext : DbContext
{
    public OrganizationsDbContext(DbContextOptions<OrganizationsDbContext> options) : base(options)
    {
    }

    public DbSet<Organization> Organizations => Set<Organization>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Organization>(entity =>
        {
            entity.Property(o => o.Name).IsRequired().HasMaxLength(200);

            // Explicit Ids (not auto-increment leftovers) — Day 33's Employees
            // module and Day 40's WorkOrders module both still assume
            // OrganizationId 1/2 refer to real organizations (ADR 0002: they
            // only ever store a plain int, never validate it themselves).
            // This is a brand-new database, so — unlike RoadmapOS Day 9's
            // HasData avoidance (which protected already-existing organic
            // data) — there's no collision risk here.
            entity.HasData(
                new Organization("Acme Field Services") { Id = 1 },
                new Organization("Blue Ridge Maintenance") { Id = 2 }
            );
        });
    }
}
