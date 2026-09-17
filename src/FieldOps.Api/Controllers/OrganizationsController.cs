using FieldOps.Api.Models;
using FieldOps.Modules.Organizations;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrganizationsController : ControllerBase
{
    private readonly IOrganizationDirectory _organizationDirectory;

    public OrganizationsController(IOrganizationDirectory organizationDirectory)
    {
        _organizationDirectory = organizationDirectory;
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<OrganizationDto>> GetAll()
    {
        // Mapping the module's own public shape (OrganizationSummary) into
        // this API's own HTTP-facing DTO — two separate boundaries (module
        // contract vs. HTTP contract) kept explicit even though they happen
        // to look identical today, the same discipline StockPilot applied
        // between its store layer and its HTTP DTOs.
        var organizations = _organizationDirectory.GetAll();
        var dtos = organizations.Select(o => new OrganizationDto(o.Id, o.Name)).ToList();
        return Ok(dtos);
    }

    [HttpGet("{id}")]
    public ActionResult<OrganizationDto> GetById(int id)
    {
        var organization = _organizationDirectory.GetById(id);
        if (organization == null)
        {
            return NotFound();
        }

        return Ok(new OrganizationDto(organization.Id, organization.Name));
    }
}
