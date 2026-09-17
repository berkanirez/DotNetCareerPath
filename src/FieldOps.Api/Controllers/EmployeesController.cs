using FieldOps.Api.Application;
using FieldOps.Api.Models;
using FieldOps.Modules.Employees;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmployeesController : ControllerBase
{
    private readonly IEmployeeDirectory _employeeDirectory;
    private readonly EmployeeApplicationService _employeeApplicationService;

    public EmployeesController(IEmployeeDirectory employeeDirectory, EmployeeApplicationService employeeApplicationService)
    {
        _employeeDirectory = employeeDirectory;
        _employeeApplicationService = employeeApplicationService;
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
        // The cross-module orchestration (does this organization exist? if
        // so, create the employee) now lives entirely in
        // EmployeeApplicationService (Day 34) — this action's only job is
        // translating that plain result into an HTTP response.
        var result = _employeeApplicationService.CreateEmployee(request.Name, request.OrganizationId);
        if (!result.Succeeded)
        {
            return BadRequest(result.Error);
        }

        var dto = new EmployeeDto(result.Employee!.Id, result.Employee.Name, result.Employee.OrganizationId);
        // No single-employee GetById action exists yet (out of today's scope,
        // which is the cross-module orchestration, not full Employees CRUD),
        // so there's no correct target for a Location header via
        // CreatedAtAction — 201 is returned directly instead.
        return StatusCode(StatusCodes.Status201Created, dto);
    }
}
