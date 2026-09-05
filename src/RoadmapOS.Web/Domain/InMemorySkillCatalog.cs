namespace RoadmapOS.Web.Domain;

public class InMemorySkillCatalog : ISkillCatalog
{
    private readonly List<Skill> _skills = new()
    {
        new("C#", "Language", SkillLevel.CanImplementWithGuidance, SkillLevel.CanExplainProduction) { Id = 1 },
        new("ASP.NET Core", "Framework", SkillLevel.CanExplainPurpose, SkillLevel.CanImplementIndependently) { Id = 2 },
        new("EF Core", "Framework", SkillLevel.NotStudied, SkillLevel.CanImplementIndependently)
        {
            Id = 3,
            Notes = "Not started yet"
        },
        new("SQL Server", "Database", SkillLevel.NotStudied, SkillLevel.CanImplementWithGuidance) { Id = 4 }
    };

    public IReadOnlyList<Skill> GetAll()
    {
        var sorted = new List<Skill>(_skills);
        sorted.Sort();
        return sorted;
    }

    public Skill? GetById(int id)
    {
        foreach (var skill in _skills)
        {
            if (skill.Id == id)
            {
                return skill;
            }
        }

        return null;
    }

    public void Add(Skill skill)
    {
        var nextId = 1;
        foreach (var existing in _skills)
        {
            if (existing.Id >= nextId)
            {
                nextId = existing.Id + 1;
            }
        }

        skill.Id = nextId;
        _skills.Add(skill);
    }

    public void Update(Skill skill)
    {
        // No-op: 'skill' returned by GetById is the same in-memory instance
        // already stored in _skills, so changes to it are already reflected.
    }
}
