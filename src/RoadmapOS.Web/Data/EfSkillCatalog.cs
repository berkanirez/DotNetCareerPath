using RoadmapOS.Web.Domain;

namespace RoadmapOS.Web.Data;

public class EfSkillCatalog : ISkillCatalog
{
    private readonly RoadmapOSDbContext _context;

    public EfSkillCatalog(RoadmapOSDbContext context)
    {
        _context = context;
    }

    public IReadOnlyList<Skill> GetAll()
    {
        var skills = _context.Skills.ToList();
        skills.Sort();
        return skills;
    }
}
