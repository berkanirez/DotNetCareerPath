namespace RoadmapOS.Web.Domain;

public class ProgressCalculator
{
    public double CalculateOverallProgress(IReadOnlyList<Skill> skills)
    {
        if (skills.Count == 0)
        {
            return 0;
        }

        var currentSum = 0;
        var targetSum = 0;

        foreach (var skill in skills)
        {
            currentSum += (int)skill.CurrentLevel;
            targetSum += (int)skill.TargetLevel;
        }

        if (targetSum == 0)
        {
            return 0;
        }

        var rawPercentage = (double)currentSum / targetSum * 100;

        return Math.Min(100, rawPercentage);
    }

    public IReadOnlyList<CategoryProgress> CalculateCategoryProgress(IReadOnlyList<Skill> skills)
    {
        return skills
            .GroupBy(skill => skill.Category)
            .Select(group => new CategoryProgress(group.Key, CalculateOverallProgress(group.ToList())))
            .ToList();
    }
}
