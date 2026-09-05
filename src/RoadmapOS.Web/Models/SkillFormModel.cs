using System.ComponentModel.DataAnnotations;
using RoadmapOS.Web.Domain;

namespace RoadmapOS.Web.Models;

public class SkillFormModel
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Category { get; set; } = string.Empty;

    [Required]
    public SkillLevel CurrentLevel { get; set; }

    [Required]
    public SkillLevel TargetLevel { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
