namespace RoadmapOS.Web.Domain;

public interface ISkillCatalog
{
    IReadOnlyList<Skill> GetAll();
    Skill? GetById(int id);
    void Add(Skill skill);
    void Update(Skill skill);
}
