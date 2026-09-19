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

    // Day 42: neither of these takes an employeeId. Unlike Assign (a
    // cross-module fact — is this employee real, is it in the right
    // organization), "is the caller the one this work order was assigned
    // to" only needs a WorkOrder's own AssignedEmployeeId field — the host
    // (WorkOrdersController) checks that itself before calling here, the
    // same place Day 37's role checks live. This module only enforces its
    // own state-machine invariant: Start requires Assigned, Complete
    // requires InProgress. Returns null if the work order doesn't exist or
    // isn't in the required prior state.
    WorkOrderSummary? Start(int workOrderId);
    WorkOrderSummary? Complete(int workOrderId);

    // Day 43: changes WHO is assigned without changing Status — unlike
    // Assign (Open -> Assigned), Reassign only makes sense while a work
    // order is already Assigned or InProgress (someone was doing it, now
    // someone else will). Returns null if the work order doesn't exist or
    // isn't currently in one of those two states.
    WorkOrderSummary? Reassign(int workOrderId, int newEmployeeId);

    // Day 45: clears the assignment entirely and returns to Open — the
    // simplest of the four mutations, since (unlike Assign/Reassign) there's
    // no new employeeId to validate at all, no cross-module fact needed.
    // Same prior-state requirement as Reassign (Assigned or InProgress);
    // unassigning an already-Open or Completed work order makes no sense.
    WorkOrderSummary? Unassign(int workOrderId);

    // Day 45 independent-task addition: without this, Completed was a
    // permanent dead end — no transition anywhere accepted it as a starting
    // state. Reopens back to InProgress (not Open/Assigned) since the
    // original assignee is still on record and simply resumes; returns
    // null if the work order doesn't exist or isn't currently Completed.
    WorkOrderSummary? Reopen(int workOrderId);
}
