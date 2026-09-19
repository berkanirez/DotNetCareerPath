namespace FieldOps.Modules.WorkOrders;

// Deliberately does NOT validate that organizationId refers to a real
// Organization — same reasoning as IEmployeeDirectory (Day 33, ADR 0002):
// this module has no reference to FieldOps.Modules.Organizations at all.
// That validation, and the caller's membership within that organization,
// is the host's job (see FieldOps.Api's WorkOrdersController).
public interface IWorkOrderDirectory
{
    IReadOnlyList<WorkOrderSummary> GetAll();
    WorkOrderSummary? GetById(int id);
    WorkOrderSummary Create(string title, int organizationId);

    // Deliberately does NOT validate that employeeId refers to a real
    // Employee, or that it belongs to the same organization as this work
    // order — same ADR 0002 reasoning as Create's organizationId. Those are
    // cross-module facts only the host can check (WorkOrderAssignmentService).
    // The "must currently be Open" rule is different: it's a fact purely
    // about a WorkOrder's own state, so this module enforces it itself
    // rather than trusting the host to remember. Returns null if no work
    // order with this id exists, or if it isn't currently Open.
    WorkOrderSummary? Assign(int workOrderId, int employeeId);
}
