using FieldOps.Api.Models;
using FieldOps.Modules.Employees;
using FieldOps.Modules.WorkOrders;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Controllers;

// Day 40: the tenant-isolation + membership pattern Week 8 discovered
// through real, live-found vulnerabilities (Days 35, 38, 39) is applied
// here from the start, not retrofitted after an exploit. Every action
// requires X-Organization-Id (which tenant) and X-Employee-Id (who, within
// that tenant), and verifies the acting employee's own OrganizationId
// matches the one being acted on — exactly EmployeesController's Day 39
// shape, this time correct from day one.
[ApiController]
[Route("api/[controller]")]
public class WorkOrdersController : ControllerBase
{
    private readonly IWorkOrderDirectory _workOrderDirectory;
    private readonly IEmployeeDirectory _employeeDirectory;

    public WorkOrdersController(IWorkOrderDirectory workOrderDirectory, IEmployeeDirectory employeeDirectory)
    {
        _workOrderDirectory = workOrderDirectory;
        _employeeDirectory = employeeDirectory;
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<WorkOrderDto>> GetAll(
        [FromHeader(Name = "X-Organization-Id")] int? organizationId,
        [FromHeader(Name = "X-Employee-Id")] int? actingEmployeeId)
    {
        var membershipError = ValidateMembership(organizationId, actingEmployeeId);
        if (membershipError is not null)
        {
            return membershipError;
        }

        var workOrders = _workOrderDirectory.GetAll()
            .Where(w => w.OrganizationId == organizationId)
            .Select(w => new WorkOrderDto(w.Id, w.Title, w.OrganizationId, w.Status))
            .ToList();

        return Ok(workOrders);
    }

    [HttpPost]
    public ActionResult<WorkOrderDto> Create(
        CreateWorkOrderRequest request,
        [FromHeader(Name = "X-Organization-Id")] int? organizationId,
        [FromHeader(Name = "X-Employee-Id")] int? actingEmployeeId)
    {
        var membershipError = ValidateMembership(organizationId, actingEmployeeId);
        if (membershipError is not null)
        {
            return membershipError;
        }

        var workOrder = _workOrderDirectory.Create(request.Title, organizationId!.Value);
        var dto = new WorkOrderDto(workOrder.Id, workOrder.Title, workOrder.OrganizationId, workOrder.Status);
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    // Shared by both actions today — unlike EmployeesController (Day 39),
    // where the identical duplication between Create/GetAll was deliberately
    // left alone (only two call sites, "rule of three" not yet met), this
    // controller starts with two call sites needing the EXACT same check
    // from day one, with no additional per-action check layered on top
    // (EmployeesController.Create's extra role check is what made its two
    // call sites non-identical). Extracting here avoids writing the same
    // three checks twice within a single new file.
    private ActionResult? ValidateMembership(int? organizationId, int? actingEmployeeId)
    {
        if (organizationId is null)
        {
            return BadRequest("X-Organization-Id header is required.");
        }

        if (actingEmployeeId is null)
        {
            return BadRequest("X-Employee-Id header is required.");
        }

        var actingEmployee = _employeeDirectory.GetById(actingEmployeeId.Value);
        if (actingEmployee is null)
        {
            return BadRequest($"Employee {actingEmployeeId} does not exist.");
        }

        if (actingEmployee.OrganizationId != organizationId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, "You can only act within your own organization.");
        }

        return null;
    }
}
