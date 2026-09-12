using RoadmapOS.Web.Domain;

namespace RoadmapOS.Web.Tests;

public class ProgressCalculatorTests
{
    private readonly ProgressCalculator _calculator = new();

    [Fact]
    public void CalculateOverallProgress_EmptyList_ReturnsZero()
    {
        var skills = new List<Skill>();

        var result = _calculator.CalculateOverallProgress(skills);

        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateOverallProgress_SingleSkillAtTarget_ReturnsHundred()
    {
        var skills = new List<Skill>
        {
            new("C#", "Language", SkillLevel.CanImplementIndependently, SkillLevel.CanImplementIndependently)
        };

        var result = _calculator.CalculateOverallProgress(skills);

        Assert.Equal(100, result);
    }

    [Fact]
    public void CalculateOverallProgress_MultipleSkillsPartialProgress_ReturnsWeightedPercentage()
    {
        var skills = new List<Skill>
        {
            new("C#", "Language", SkillLevel.CanImplementWithGuidance, SkillLevel.CanExplainProduction),
            new("ASP.NET Core", "Framework", SkillLevel.CanImplementIndependently, SkillLevel.CanImplementIndependently)
        };

        var result = _calculator.CalculateOverallProgress(skills);

        Assert.Equal(71.43, Math.Round(result, 2));
    }

    [Fact]
    public void CalculateOverallProgress_SkillExceedsTarget_ClampsAtHundred()
    {
        var skills = new List<Skill>
        {
            new("C#", "Language", SkillLevel.CanExplainProduction, SkillLevel.CanExplainPurpose)
        };

        var result = _calculator.CalculateOverallProgress(skills);

        Assert.Equal(100, result);
    }

    [Fact]
    public void CalculateOverallProgress_AllTargetsAreNotStudied_ReturnsZeroWithoutDivideByZero()
    {
        var skills = new List<Skill>
        {
            new("C#", "Language", SkillLevel.NotStudied, SkillLevel.NotStudied),
            new("ASP.NET Core", "Framework", SkillLevel.NotStudied, SkillLevel.NotStudied)
        };

        var result = _calculator.CalculateOverallProgress(skills);

        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateOverallProgress__OneSkillHasNoTarget_ReturnsHundred()
    {
        var skills = new List<Skill>
        {
            new("Skill A", "Language", SkillLevel.CanImplementWithGuidance, SkillLevel.NotStudied),
            new("Skill B", "Framework", SkillLevel.CanExplainPurpose, SkillLevel.CanImplementIndependently)
        };
        var result = _calculator.CalculateOverallProgress(skills);
        Assert.Equal(100, result);
    }

    [Fact]
    public void CalculateCategoryProgress_EmptyList_ReturnsEmptyResult()
    {
        var skills = new List<Skill>();

        var result = _calculator.CalculateCategoryProgress(skills);

        Assert.Empty(result);
    }

    [Fact]
    public void CalculateCategoryProgress_TwoCategories_ReturnsOnePercentagePerCategory()
    {
        var skills = new List<Skill>
        {
            new("C#", "Language", SkillLevel.CanImplementWithGuidance, SkillLevel.CanExplainProduction),
            new("ASP.NET Core", "Framework", SkillLevel.CanExplainPurpose, SkillLevel.CanImplementIndependently),
            new("EF Core", "Framework", SkillLevel.NotStudied, SkillLevel.CanImplementIndependently)
        };

        var result = _calculator.CalculateCategoryProgress(skills);

        Assert.Equal(2, result.Count);

        var language = result.Single(c => c.Category == "Language");
        Assert.Equal(50, language.Percentage);

        var framework = result.Single(c => c.Category == "Framework");
        Assert.Equal(16.67, Math.Round(framework.Percentage, 2));
    }
}
