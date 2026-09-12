namespace RoadmapOS.Web.Domain;

public class Evidence
{
    public int Id { get; set; }
    public string Description { get; set; }
    public DateOnly RecordedOn { get; set; }
    public int SkillId { get; set; }
    public Skill? Skill { get; set; }

    public Evidence(string description, DateOnly recordedOn, int skillId)
    {
        Description = description;
        RecordedOn = recordedOn;
        SkillId = skillId;
    }
}
