using Microsoft.EntityFrameworkCore;
using RoadmapOS.Web.Domain;

namespace RoadmapOS.Web.Data;

public class RoadmapOSDbContext : DbContext
{
    public RoadmapOSDbContext(DbContextOptions<RoadmapOSDbContext> options) : base(options)
    {
    }

    public DbSet<Skill> Skills => Set<Skill>();
}
