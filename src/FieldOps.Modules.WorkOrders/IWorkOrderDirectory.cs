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

    // Day 47: customerId is optional and, like organizationId, not
    // validated here — this module has no reference to
    // FieldOps.Modules.Customers at all (ADR 0002). The host
    // (WorkOrdersController) checks the customer exists and belongs to the
    // right organization before ever calling this.
    WorkOrderSummary Create(string title, int organizationId, int? customerId = null);

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

    // Day 71: outboxEventType/outboxPayload are opaque to this module — it
    // never interprets them, just persists them in the SAME SaveChanges
    // call as the Status change (the Outbox pattern's actual guarantee).
    // The host builds these two strings (typically nameof(SomeEvent) and
    // JsonSerializer.Serialize(someEvent)) since only the host knows what a
    // "WorkOrderCompletedEvent" even is (ADR 0002 — no FieldOps.Api type
    // reference exists in this module).
    WorkOrderSummary? Complete(int workOrderId, string outboxEventType, string outboxPayload);

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

    // Day 46: "must not be Open" is the only state rule — nobody's doing
    // anything yet if nobody's assigned, so there's nothing to attach
    // evidence to. Every other status (Assigned/InProgress/Completed) is
    // valid, unlike the other mutations which each need one exact prior
    // state. Returns null if the work order doesn't exist or is still Open.
    WorkOrderSummary? AddEvidence(int workOrderId, string note);

    // Day 47: only enforces the state-machine invariant (must be Completed)
    // — same split as Start/Complete/Reassign/Unassign: "is the caller
    // actually the linked customer" is a host-level check (the host has
    // ICustomerDirectory, this module never will). Returns null if the
    // work order doesn't exist or isn't Completed.
    WorkOrderSummary? Approve(int workOrderId);

    // Day 71: read side of the Outbox pattern — OutboxPublisher (a host
    // BackgroundService) polls this to find rows Complete() wrote but
    // nothing has published to RabbitMQ yet.
    IReadOnlyList<OutboxMessageSummary> GetUnpublishedOutboxMessages();

    // Marks one row published so it's never picked up again. A separate
    // call (not part of Complete) since publishing happens later, in a
    // different scope/transaction, once IEventPublisher actually succeeds.
    void MarkOutboxMessagePublished(int outboxMessageId);
}
