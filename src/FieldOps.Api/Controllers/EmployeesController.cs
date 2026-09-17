using FieldOps.Api.Models;
using FieldOps.Modules.Employees;
using FieldOps.Modules.Organizations;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmployeesController : ControllerBase
{
    private readonly IEmployeeDirectory _employeeDirectory;
    private readonly IOrganizationDirectory _organizationDirectory;

    public EmployeesController(IEmployeeDirectory employeeDirectory, IOrganizationDirectory organizationDirectory)
    {
        _employeeDirectory = employeeDirectory;
        _organizationDirectory = organizationDirectory;
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<EmployeeDto>> GetAll([FromQuery] int? organizationId = null)
    {
        var employees = _employeeDirectory.GetAll().AsEnumerable();

        if (organizationId is not null)
        {
            employees = employees.Where(e => e.OrganizationId == organizationId);
        }

        var dtos = employees.Select(e => new EmployeeDto(e.Id, e.Name, e.OrganizationId)).ToList();
        return Ok(dtos);
    }

    [HttpPost]
    public ActionResult<EmployeeDto> Create(CreateEmployeeRequest request)
    {
        // The host, not the Employees module, is responsible for validating
        // that OrganizationId refers to a real Organization — Employees has
        // no way to check this itself (ADR 0002: no reference between the
        // two modules at all). This is the "orchestration" role the host
        // plays whenever more than one module is involved in a single request.
        var organization = _organizationDirectory.GetById(request.OrganizationId);
        if (organization is null)
        {
            return BadRequest($"Organization {request.OrganizationId} does not exist.");
        }

        var employee = _employeeDirectory.Create(request.Name, request.OrganizationId);
        var dto = new EmployeeDto(employee.Id, employee.Name, employee.OrganizationId);
        // No single-employee GetById action exists yet (out of today's scope,
        // which is the cross-module orchestration, not full Employees CRUD),
        // so there's no correct target for a Location header via
        // CreatedAtAction — 201 is returned directly instead.
        return StatusCode(StatusCodes.Status201Created, dto);
    }
}
