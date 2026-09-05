namespace RoadmapOS.Web.Domain;

public class RoadmapPhase
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int Order { get; set; }
    public List<Project> Projects { get; set; } = new();

    public RoadmapPhase(string name, int order)
    {
        Name = name;
        Order = order;
    }
}
