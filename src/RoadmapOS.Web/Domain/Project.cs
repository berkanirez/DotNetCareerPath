namespace RoadmapOS.Web.Domain;

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int RoadmapPhaseId { get; set; }
    public RoadmapPhase? RoadmapPhase { get; set; }
    public List<Milestone> Milestones { get; set; } = new();

    public Project(string name, int roadmapPhaseId)
    {
        Name = name;
        RoadmapPhaseId = roadmapPhaseId;
    }
}
