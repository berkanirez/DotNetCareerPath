using Microsoft.AspNetCore.Mvc;
using RoadmapOS.Web.Domain;

namespace RoadmapOS.Web.Controllers;

public class SkillsController : Controller
{
    private readonly ISkillCatalog _skillCatalog;

    public SkillsController(ISkillCatalog skillCatalog)
    {
        _skillCatalog = skillCatalog;
    }

    public IActionResult Index()
    {
        var skills = _skillCatalog.GetAll();
        return View(skills);
    }
}
