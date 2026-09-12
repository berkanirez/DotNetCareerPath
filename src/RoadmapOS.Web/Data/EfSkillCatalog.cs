using Microsoft.EntityFrameworkCore;
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
        var skills = _context.Skills.Include(s => s.EvidenceRecords).ToList();
        skills.Sort();
        return skills;
    }

    public Skill? GetById(int id)
    {
        return _context.Skills.Find(id);
    }

    public void Add(Skill skill)
    {
        _context.Skills.Add(skill);
        _context.SaveChanges();
    }

    public void Update(Skill skill)
    {
        // 'skill' was already loaded (and is being tracked) by this same DbContext
        // via GetById earlier in the same request, so its changed properties are
        // already known to the change tracker — SaveChanges() is all that's needed.
        _context.SaveChanges();
    }
}
