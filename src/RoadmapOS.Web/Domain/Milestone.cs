namespace RoadmapOS.Web.Domain;

public class Milestone
{
    public int Id { get; set; }
    public string Name { get; set; }
    public bool IsCompleted { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public Milestone(string name, int projectId)
    {
        Name = name;
        ProjectId = projectId;
    }
}
