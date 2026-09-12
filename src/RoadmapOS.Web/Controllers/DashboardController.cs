using Microsoft.AspNetCore.Mvc;
using RoadmapOS.Web.Domain;
using RoadmapOS.Web.Models;

namespace RoadmapOS.Web.Controllers;

public class DashboardController : Controller
{
    private readonly ISkillCatalog _skillCatalog;
    private readonly ProgressCalculator _progressCalculator;

    public DashboardController(ISkillCatalog skillCatalog, ProgressCalculator progressCalculator)
    {
        _skillCatalog = skillCatalog;
        _progressCalculator = progressCalculator;
    }

    public IActionResult Index()
    {
        var skills = _skillCatalog.GetAll();
        var overallProgress = _progressCalculator.CalculateOverallProgress(skills);
        var categoryProgress = _progressCalculator.CalculateCategoryProgress(skills);

        var viewModel = new DashboardViewModel(overallProgress, categoryProgress);

        return View(viewModel);
    }
}
