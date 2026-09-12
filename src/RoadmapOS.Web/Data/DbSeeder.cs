using RoadmapOS.Web.Domain;

namespace RoadmapOS.Web.Data;

// Runtime, idempotent seeder — chosen over EF Core's migration-based HasData()
// because real data (added/edited through the app itself) already exists in
// this database. HasData bakes fixed IDs into a migration, which risks
// colliding with organically-created rows; this approach only inserts when
// a table is still empty.
public static class DbSeeder
{
    public static void Seed(RoadmapOSDbContext context)
    {
        SeedSkills(context);
        SeedRoadmap(context);
        SeedEvidence(context);
    }

    private static void SeedSkills(RoadmapOSDbContext context)
    {
        if (context.Skills.Any())
        {
            return;
        }

        context.Skills.AddRange(
            new Skill("C#", "Language", SkillLevel.CanImplementWithGuidance, SkillLevel.CanExplainProduction),
            new Skill("ASP.NET Core", "Framework", SkillLevel.CanExplainPurpose, SkillLevel.CanImplementIndependently),
            new Skill("EF Core", "Framework", SkillLevel.NotStudied, SkillLevel.CanImplementIndependently)
            {
                Notes = "Not started yet"
            },
            new Skill("SQL Server", "Database", SkillLevel.NotStudied, SkillLevel.CanImplementWithGuidance)
        );
        context.SaveChanges();
    }

    private static void SeedRoadmap(RoadmapOSDbContext context)
    {
        if (context.RoadmapPhases.Any())
        {
            return;
        }

        var phase = new RoadmapPhase("Phase 1 — RoadmapOS", 1);
        context.RoadmapPhases.Add(phase);
        context.SaveChanges();

        var project = new Project("RoadmapOS", phase.Id);
        context.Projects.Add(project);
        context.SaveChanges();

        context.Milestones.AddRange(
            new Milestone("Day 1-5: environment, domain model, EF Core, create/edit flow", project.Id) { IsCompleted = true },
            new Milestone("Day 6: relationships and database constraints", project.Id) { IsCompleted = true },
            new Milestone("Day 7-10: domain service, LINQ, evidence tracking, release", project.Id) { IsCompleted = false }
        );
        context.SaveChanges();
    }

    private static void SeedEvidence(RoadmapOSDbContext context)
    {
        if (context.EvidenceRecords.Any())
        {
            return;
        }

        var csharpSkill = context.Skills.FirstOrDefault(s => s.Name == "C#");
        var efCoreSkill = context.Skills.FirstOrDefault(s => s.Name == "EF Core");

        if (csharpSkill is not null)
        {
            context.EvidenceRecords.Add(new Evidence(
                "Skill.cs, SkillLevel.cs, SkillSnapshot.cs implemented and verified (Day 2)",
                new DateOnly(2026, 9, 1),
                csharpSkill.Id));
        }

        if (efCoreSkill is not null)
        {
            context.EvidenceRecords.Add(new Evidence(
                "RoadmapOSDbContext, first migration and EfSkillCatalog implemented (Day 4)",
                new DateOnly(2026, 9, 5),
                efCoreSkill.Id));
        }

        context.SaveChanges();
    }
}
