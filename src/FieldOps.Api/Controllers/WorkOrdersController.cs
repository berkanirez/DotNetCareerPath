using FieldOps.Api.Application;
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
    private readonly WorkOrderAssignmentService _workOrderAssignmentService;

    public WorkOrdersController(
        IWorkOrderDirectory workOrderDirectory,
        IEmployeeDirectory employeeDirectory,
        WorkOrderAssignmentService workOrderAssignmentService)
    {
        _workOrderDirectory = workOrderDirectory;
        _employeeDirectory = employeeDirectory;
        _workOrderAssignmentService = workOrderAssignmentService;
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
            .Select(ToDto)
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
        var dto = ToDto(workOrder);
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    // Day 41: assignment is a state-changing action, not a read — unlike
    // GetAll's still-open "should a Member see the roster" question (Day 39),
    // "should any Member be able to assign any work order" isn't genuinely
    // ambiguous, so this reuses Create's Day 37 Admin-only precedent directly.
    [HttpPost("{id}/assign")]
    public ActionResult<WorkOrderDto> Assign(
        int id,
        AssignWorkOrderRequest request,
        [FromHeader(Name = "X-Organization-Id")] int? organizationId,
        [FromHeader(Name = "X-Employee-Id")] int? actingEmployeeId)
    {
        var membershipError = ValidateMembership(organizationId, actingEmployeeId);
        if (membershipError is not null)
        {
            return membershipError;
        }

        // ValidateMembership already looked this employee up once — looked
        // up again here since only this one action needs the role, and
        // adding an out-parameter to ValidateMembership purely for this
        // single caller would complicate a helper the other two actions
        // don't need changed. A real, negligible cost against an in-memory
        // list; worth revisiting once a real database makes lookups non-free.
        var actingEmployee = _employeeDirectory.GetById(actingEmployeeId!.Value)!;
        if (actingEmployee.Role != EmployeeRole.Admin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Only an Admin can assign work orders.");
        }

        var result = _workOrderAssignmentService.AssignWorkOrder(id, request.EmployeeId, organizationId!.Value);
        if (!result.Succeeded)
        {
            return BadRequest(result.Error);
        }

        return Ok(ToDto(result.WorkOrder!));
    }

    private static WorkOrderDto ToDto(WorkOrderSummary workOrder) =>
        new(workOrder.Id, workOrder.Title, workOrder.OrganizationId, workOrder.Status, workOrder.AssignedEmployeeId);

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
