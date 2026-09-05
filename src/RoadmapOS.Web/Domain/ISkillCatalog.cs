namespace RoadmapOS.Web.Domain;

public interface ISkillCatalog
{
    IReadOnlyList<Skill> GetAll();
}
