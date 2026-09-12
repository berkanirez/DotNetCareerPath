using RoadmapOS.Web.Domain;

namespace RoadmapOS.Web.Models;

public record DashboardViewModel(double OverallProgress, IReadOnlyList<CategoryProgress> CategoryProgress);
