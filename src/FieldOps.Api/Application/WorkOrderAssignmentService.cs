using FieldOps.Modules.Employees;
using FieldOps.Modules.WorkOrders;

namespace FieldOps.Api.Application;

// Same justification as EmployeeApplicationService (Day 34): real
// cross-module coordination (needs both IWorkOrderDirectory and
// IEmployeeDirectory) plus a genuine business rule (the assignee must
// belong to the work order's own organization) — not extracted just
// because it "sounds like a service."
public class WorkOrderAssignmentService
{
    private readonly IWorkOrderDirectory _workOrderDirectory;
    private readonly IEmployeeDirectory _employeeDirectory;

    public WorkOrderAssignmentService(IWorkOrderDirectory workOrderDirectory, IEmployeeDirectory employeeDirectory)
    {
        _workOrderDirectory = workOrderDirectory;
        _employeeDirectory = employeeDirectory;
    }

    public WorkOrderAssignmentResult AssignWorkOrder(int workOrderId, int employeeId, int callerOrganizationId)
    {
        var workOrder = _workOrderDirectory.GetById(workOrderId);

        // A work order that doesn't exist and one that exists but belongs to
        // a different organization return the IDENTICAL message on purpose —
        // applying Day 37/38's lesson from the start this time, instead of
        // discovering it as a live exploit: a caller outside a work order's
        // organization should never be able to tell the two cases apart,
        // which would otherwise let them probe for valid work order ids
        // belonging to other tenants.
        if (workOrder is null || workOrder.OrganizationId != callerOrganizationId)
        {
            return WorkOrderAssignmentResult.Failure($"Work order {workOrderId} does not exist.");
        }

        // Same information-hiding principle as the work order check above,
        // now applied consistently: a genuinely missing employee and one
        // that exists but belongs to a different organization are
        // indistinguishable to the caller. Without this, an Admin could
        // probe arbitrary employeeId values via this endpoint to discover
        // which ids exist somewhere in the system, even outside their own
        // organization — the exact same class of leak Day 37/38 closed for
        // organization ids, just reachable through a different field.
        var employee = _employeeDirectory.GetById(employeeId);
        if (employee is null || employee.OrganizationId != workOrder.OrganizationId)
        {
            return WorkOrderAssignmentResult.Failure($"Employee {employeeId} does not exist.");
        }

        if (workOrder.Status != WorkOrderStatus.Open)
        {
            return WorkOrderAssignmentResult.Failure($"Work order {workOrderId} is not open for assignment.");
        }

        var assigned = _workOrderDirectory.Assign(workOrderId, employeeId);
        return WorkOrderAssignmentResult.Success(assigned!);
    }
}
