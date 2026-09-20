using FieldOps.Modules.WorkOrders.Domain;

namespace FieldOps.Modules.WorkOrders;

// internal, not public: the host must never be able to name this concrete
// class directly (only IWorkOrderDirectory) — the same enforced boundary
// as InMemoryOrganizationDirectory (Day 32) and InMemoryEmployeeDirectory
// (Day 33). No seed data — unlike Day 37's Employees seeding, there's no
// bootstrap problem here: creating a work order only requires organization
// + employee membership, both of which are already seeded elsewhere.
internal class InMemoryWorkOrderDirectory : IWorkOrderDirectory
{
    private readonly List<WorkOrder> _workOrders = new();
    private int _nextId = 1;

    public IReadOnlyList<WorkOrderSummary> GetAll()
    {
        return _workOrders.Select(ToSummary).ToList();
    }

    public WorkOrderSummary? GetById(int id)
    {
        var workOrder = _workOrders.FirstOrDefault(w => w.Id == id);
        return workOrder is null ? null : ToSummary(workOrder);
    }

    public WorkOrderSummary Create(string title, int organizationId, int? customerId = null)
    {
        var workOrder = new WorkOrder(title, organizationId, WorkOrderStatus.Open) { Id = _nextId++, CustomerId = customerId };
        _workOrders.Add(workOrder);
        return ToSummary(workOrder);
    }

    public WorkOrderSummary? Assign(int workOrderId, int employeeId)
    {
        var workOrder = _workOrders.FirstOrDefault(w => w.Id == workOrderId);
        if (workOrder is null || workOrder.Status != WorkOrderStatus.Open)
        {
            return null;
        }

        workOrder.Status = WorkOrderStatus.Assigned;
        workOrder.AssignedEmployeeId = employeeId;
        return ToSummary(workOrder);
    }

    public WorkOrderSummary? Start(int workOrderId)
    {
        var workOrder = _workOrders.FirstOrDefault(w => w.Id == workOrderId);
        if (workOrder is null || workOrder.Status != WorkOrderStatus.Assigned)
        {
            return null;
        }

        workOrder.Status = WorkOrderStatus.InProgress;
        return ToSummary(workOrder);
    }

    public WorkOrderSummary? Complete(int workOrderId)
    {
        var workOrder = _workOrders.FirstOrDefault(w => w.Id == workOrderId);
        if (workOrder is null || workOrder.Status != WorkOrderStatus.InProgress)
        {
            return null;
        }

        workOrder.Status = WorkOrderStatus.Completed;
        return ToSummary(workOrder);
    }

    public WorkOrderSummary? Reassign(int workOrderId, int newEmployeeId)
    {
        var workOrder = _workOrders.FirstOrDefault(w => w.Id == workOrderId);
        if (workOrder is null || (workOrder.Status != WorkOrderStatus.Assigned && workOrder.Status != WorkOrderStatus.InProgress))
        {
            return null;
        }

        workOrder.AssignedEmployeeId = newEmployeeId;
        return ToSummary(workOrder);
    }

    public WorkOrderSummary? Unassign(int workOrderId)
    {
        var workOrder = _workOrders.FirstOrDefault(w => w.Id == workOrderId);
        if (workOrder is null || (workOrder.Status != WorkOrderStatus.Assigned && workOrder.Status != WorkOrderStatus.InProgress))
        {
            return null;
        }

        workOrder.Status = WorkOrderStatus.Open;
        workOrder.AssignedEmployeeId = null;
        return ToSummary(workOrder);
    }

    public WorkOrderSummary? Reopen(int workOrderId)
    {
        var workOrder = _workOrders.FirstOrDefault(w => w.Id == workOrderId);
        if (workOrder is null || workOrder.Status != WorkOrderStatus.Completed)
        {
            return null;
        }

        workOrder.Status = WorkOrderStatus.InProgress;
        return ToSummary(workOrder);
    }

    public WorkOrderSummary? AddEvidence(int workOrderId, string note)
    {
        var workOrder = _workOrders.FirstOrDefault(w => w.Id == workOrderId);
        if (workOrder is null || workOrder.Status == WorkOrderStatus.Open)
        {
            return null;
        }

        workOrder.EvidenceNotes.Add(note);
        return ToSummary(workOrder);
    }

    public WorkOrderSummary? Approve(int workOrderId)
    {
        var workOrder = _workOrders.FirstOrDefault(w => w.Id == workOrderId);
        if (workOrder is null || workOrder.Status != WorkOrderStatus.Completed)
        {
            return null;
        }

        workOrder.CustomerApproved = true;
        return ToSummary(workOrder);
    }

    private static WorkOrderSummary ToSummary(WorkOrder workOrder) =>
        new(workOrder.Id, workOrder.Title, workOrder.OrganizationId, workOrder.Status, workOrder.AssignedEmployeeId, workOrder.EvidenceNotes.AsReadOnly(), workOrder.CustomerId, workOrder.CustomerApproved);
}
