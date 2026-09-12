namespace RoadmapOS.Web.Domain;

public class Skill : IComparable<Skill>
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Category { get; set; }
    public SkillLevel CurrentLevel { get; set; }
    public SkillLevel TargetLevel { get; set; }
    public string? Notes { get; set; }
    public List<Evidence> EvidenceRecords { get; set; } = new();

    public Skill(string name, string category, SkillLevel currentLevel, SkillLevel targetLevel)
    {
        Name = name;
        Category = category;
        CurrentLevel = currentLevel;
        TargetLevel = targetLevel;
    }

    public bool IsAtTarget => CurrentLevel >= TargetLevel;

    public int CompareTo(Skill? other)
    {
        if (other is null)
        {
            return 1;
        }

        var levelComparison = CurrentLevel.CompareTo(other.CurrentLevel);
        return levelComparison != 0 ? levelComparison : string.Compare(Name, other.Name, StringComparison.Ordinal);
    }
}
