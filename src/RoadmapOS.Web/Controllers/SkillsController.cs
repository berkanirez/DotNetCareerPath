using Microsoft.AspNetCore.Mvc;
using RoadmapOS.Web.Domain;
using RoadmapOS.Web.Models;

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

    [HttpGet]
    public IActionResult Create()
    {
        return View(new SkillFormModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(SkillFormModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var skill = new Skill(model.Name, model.Category, model.CurrentLevel, model.TargetLevel)
        {
            Notes = model.Notes
        };

        _skillCatalog.Add(skill);

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Edit(int id)
    {
        var skill = _skillCatalog.GetById(id);
        if (skill is null)
        {
            return NotFound();
        }

        var model = new SkillFormModel
        {
            Id = skill.Id,
            Name = skill.Name,
            Category = skill.Category,
            CurrentLevel = skill.CurrentLevel,
            TargetLevel = skill.TargetLevel,
            Notes = skill.Notes
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(int id, SkillFormModel model)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var skill = _skillCatalog.GetById(id);
        if (skill is null)
        {
            return NotFound();
        }

        skill.Name = model.Name;
        skill.Category = model.Category;
        skill.CurrentLevel = model.CurrentLevel;
        skill.TargetLevel = model.TargetLevel;
        skill.Notes = model.Notes;

        _skillCatalog.Update(skill);

        return RedirectToAction(nameof(Index));
    }
}
