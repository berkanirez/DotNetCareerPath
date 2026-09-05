namespace RoadmapOS.Web.Domain;

public class InMemorySkillCatalog : ISkillCatalog
{
    private readonly List<Skill> _skills = new()
    {
        new("C#", "Language", SkillLevel.CanImplementWithGuidance, SkillLevel.CanExplainProduction),
        new("ASP.NET Core", "Framework", SkillLevel.CanExplainPurpose, SkillLevel.CanImplementIndependently),
        new("EF Core", "Framework", SkillLevel.NotStudied, SkillLevel.CanImplementIndependently)
        {
            Notes = "Not started yet"
        },
        new("SQL Server", "Database", SkillLevel.NotStudied, SkillLevel.CanImplementWithGuidance)
    };

    public IReadOnlyList<Skill> GetAll()
    {
        var sorted = new List<Skill>(_skills);
        sorted.Sort();
        return sorted;
    }
}
