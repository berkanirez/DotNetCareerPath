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

    public WorkOrderSummary Create(string title, int organizationId)
    {
        var workOrder = new WorkOrder(title, organizationId, WorkOrderStatus.Open) { Id = _nextId++ };
        _workOrders.Add(workOrder);
        return ToSummary(workOrder);
    }

    private static WorkOrderSummary ToSummary(WorkOrder workOrder) =>
        new(workOrder.Id, workOrder.Title, workOrder.OrganizationId, workOrder.Status);
}
