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

        var adminError = ValidateIsAdmin(actingEmployeeId!.Value, "assign");
        if (adminError is not null)
        {
            return adminError;
        }

        var result = _workOrderAssignmentService.AssignWorkOrder(id, request.EmployeeId, organizationId!.Value);
        if (!result.Succeeded)
        {
            return BadRequest(result.Error);
        }

        return Ok(ToDto(result.WorkOrder!));
    }

    // Day 44: reuses AssignWorkOrderRequest — identical shape (just an
    // EmployeeId), no reason for a separate ReassignWorkOrderRequest record.
    // Unlike Assign (Admin-only — deciding who gets brand-new work stays a
    // dispatcher decision), Reassign is FieldOps's first COMBINED
    // authorization rule: an Admin, OR the employee this work order is
    // currently assigned to, may hand it off — role (Day 37) OR ownership
    // (Day 42), not just one category at a time. The Assigned-or-InProgress
    // precondition (not Open, not Completed) still lives in the service.
    [HttpPost("{id}/reassign")]
    public ActionResult<WorkOrderDto> Reassign(
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

        var authError = ValidateIsAdminOrAssignee(id, organizationId, actingEmployeeId, "reassign");
        if (authError is not null)
        {
            return authError;
        }

        var result = _workOrderAssignmentService.ReassignWorkOrder(id, request.EmployeeId, organizationId!.Value);
        if (!result.Succeeded)
        {
            return BadRequest(result.Error);
        }

        return Ok(ToDto(result.WorkOrder!));
    }

    // Day 45: the simplest of the four mutations — no new employeeId to
    // validate, so no cross-module fact is needed and no
    // WorkOrderAssignmentService call is involved, unlike Assign/Reassign.
    // Reuses Day 44's ValidateIsAdminOrAssignee as-is (no new duplication):
    // an Admin, or the current assignee, may drop a work order back to
    // Open with nobody assigned.
    [HttpPost("{id}/unassign")]
    public ActionResult<WorkOrderDto> Unassign(
        int id,
        [FromHeader(Name = "X-Organization-Id")] int? organizationId,
        [FromHeader(Name = "X-Employee-Id")] int? actingEmployeeId)
    {
        var membershipError = ValidateMembership(organizationId, actingEmployeeId);
        if (membershipError is not null)
        {
            return membershipError;
        }

        var authError = ValidateIsAdminOrAssignee(id, organizationId, actingEmployeeId, "unassign");
        if (authError is not null)
        {
            return authError;
        }

        var updated = _workOrderDirectory.Unassign(id);
        if (updated is null)
        {
            return BadRequest($"Work order {id} must be Assigned or InProgress before it can be unassigned.");
        }

        return Ok(ToDto(updated));
    }

    // Day 45 independent-task addition: without this, Completed was a
    // permanent dead end. Deliberately Admin-only (reuses ValidateIsAdmin's
    // role check, Assign's precedent) rather than Reassign/Unassign's
    // combined rule — un-completing work is a higher-stakes correction (the
    // assignee already declared it done), not something the assignee
    // should be able to reverse unilaterally at will.
    //
    // A real, live-caught bug during this exact write-up: the first version
    // called ValidateIsAdmin alone, which only checks the ACTING employee's
    // role — never whether the target work order belongs to their
    // organization at all. Live-proven exploit: an Org 1 Admin could reopen
    // Org 2's completed work order just by naming its id. Assign avoids
    // this because WorkOrderAssignmentService's ValidateWorkOrderAndEmployee
    // checks the work order's organization downstream; Reopen calls
    // IWorkOrderDirectory directly with no such layer, so the check has to
    // happen here instead.
    [HttpPost("{id}/reopen")]
    public ActionResult<WorkOrderDto> Reopen(
        int id,
        [FromHeader(Name = "X-Organization-Id")] int? organizationId,
        [FromHeader(Name = "X-Employee-Id")] int? actingEmployeeId)
    {
        var membershipError = ValidateMembership(organizationId, actingEmployeeId);
        if (membershipError is not null)
        {
            return membershipError;
        }

        var adminError = ValidateIsAdminForWorkOrder(id, organizationId, actingEmployeeId, "reopen");
        if (adminError is not null)
        {
            return adminError;
        }

        var updated = _workOrderDirectory.Reopen(id);
        if (updated is null)
        {
            return BadRequest($"Work order {id} must be Completed before it can be reopened.");
        }

        return Ok(ToDto(updated));
    }

    // Day 42: ownership-based authorization — a third kind alongside Day 35's
    // tenant membership and Day 37's role. "Is the caller the specific
    // employee this work order was assigned to" isn't a group fact (any org
    // member, any Admin); it's a fact about this ONE record, so it's checked
    // here in the controller, not inside the module, using AssignedEmployeeId
    // — a field the module already exposes, no cross-module lookup needed.
    [HttpPost("{id}/start")]
    public ActionResult<WorkOrderDto> Start(
        int id,
        [FromHeader(Name = "X-Organization-Id")] int? organizationId,
        [FromHeader(Name = "X-Employee-Id")] int? actingEmployeeId)
    {
        var membershipError = ValidateMembership(organizationId, actingEmployeeId);
        if (membershipError is not null)
        {
            return membershipError;
        }

        var ownershipError = ValidateOwnership(id, organizationId, actingEmployeeId, out _);
        if (ownershipError is not null)
        {
            return ownershipError;
        }

        var updated = _workOrderDirectory.Start(id);
        if (updated is null)
        {
            return BadRequest($"Work order {id} must be Assigned before it can be started.");
        }

        return Ok(ToDto(updated));
    }

    [HttpPost("{id}/complete")]
    public ActionResult<WorkOrderDto> Complete(
        int id,
        [FromHeader(Name = "X-Organization-Id")] int? organizationId,
        [FromHeader(Name = "X-Employee-Id")] int? actingEmployeeId)
    {
        var membershipError = ValidateMembership(organizationId, actingEmployeeId);
        if (membershipError is not null)
        {
            return membershipError;
        }

        var ownershipError = ValidateOwnership(id, organizationId, actingEmployeeId, out _);
        if (ownershipError is not null)
        {
            return ownershipError;
        }

        var updated = _workOrderDirectory.Complete(id);
        if (updated is null)
        {
            return BadRequest($"Work order {id} must be InProgress before it can be completed.");
        }

        return Ok(ToDto(updated));
    }

    // Day 43: extracted for Assign specifically. Day 44 moved Reassign onto
    // ValidateIsAdminOrAssignee below (a different, combined rule), so this
    // one now has a single caller again — kept as its own named method
    // anyway since "Assign is Admin-only" is a real, standalone business
    // rule worth naming, not just inlined into Assign's action body.
    private ActionResult? ValidateIsAdmin(int actingEmployeeId, string action)
    {
        // ValidateMembership already looked this employee up once — looked
        // up again here since only Assign needs the role, and adding an
        // out-parameter to ValidateMembership purely for this one caller
        // would complicate a helper the other actions don't need changed.
        // A real, negligible cost against an in-memory list; worth
        // revisiting once a real database makes lookups non-free.
        var actingEmployee = _employeeDirectory.GetById(actingEmployeeId)!;
        if (actingEmployee.Role != EmployeeRole.Admin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, $"Only an Admin can {action} work orders.");
        }

        return null;
    }

    // Day 44: FieldOps's first combined authorization check — role OR
    // ownership, not just one category. Fetches the work order itself
    // (unlike ValidateIsAdmin, which only needs the acting employee) since
    // "are you the assignee" requires knowing who the assignee IS. Reuses
    // Day 41's generic "does not exist" message for a missing/cross-org
    // work order — the same information-hiding principle applied here too.
    private ActionResult? ValidateIsAdminOrAssignee(int workOrderId, int? organizationId, int? actingEmployeeId, string action)
    {
        var actingEmployee = _employeeDirectory.GetById(actingEmployeeId!.Value)!;

        var workOrder = _workOrderDirectory.GetById(workOrderId);
        if (workOrder is null || workOrder.OrganizationId != organizationId)
        {
            return BadRequest($"Work order {workOrderId} does not exist.");
        }

        if (actingEmployee.Role != EmployeeRole.Admin && workOrder.AssignedEmployeeId != actingEmployeeId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, $"Only an Admin or the assigned employee can {action} this work order.");
        }

        return null;
    }

    // Day 45 bug fix: Reopen's Admin-only rule, but — unlike ValidateIsAdmin
    // — also confirms the work order itself belongs to the caller's
    // organization. Deliberately kept separate from ValidateIsAdminOrAssignee
    // above rather than merged: the two aren't identical (this one has no
    // ownership OR-branch at all), so forcing them into one method would be
    // exactly the "not really the same" trap Day 39/40 warned about.
    private ActionResult? ValidateIsAdminForWorkOrder(int workOrderId, int? organizationId, int? actingEmployeeId, string action)
    {
        var actingEmployee = _employeeDirectory.GetById(actingEmployeeId!.Value)!;

        var workOrder = _workOrderDirectory.GetById(workOrderId);
        if (workOrder is null || workOrder.OrganizationId != organizationId)
        {
            return BadRequest($"Work order {workOrderId} does not exist.");
        }

        if (actingEmployee.Role != EmployeeRole.Admin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, $"Only an Admin can {action} work orders.");
        }

        return null;
    }

    // Shared by Start/Complete — same "does this work order genuinely exist,
    // for me" generic-message pattern as Day 41's Assign (a work order that
    // doesn't exist and one belonging to another organization are
    // indistinguishable), plus the new ownership check.
    private ActionResult? ValidateOwnership(int workOrderId, int? organizationId, int? actingEmployeeId, out WorkOrderSummary? workOrder)
    {
        workOrder = _workOrderDirectory.GetById(workOrderId);
        if (workOrder is null || workOrder.OrganizationId != organizationId)
        {
            workOrder = null;
            return BadRequest($"Work order {workOrderId} does not exist.");
        }

        if (workOrder.AssignedEmployeeId != actingEmployeeId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Only the assigned employee can act on this work order.");
        }

        return null;
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
