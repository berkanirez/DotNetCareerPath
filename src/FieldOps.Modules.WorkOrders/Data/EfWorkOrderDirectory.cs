using FieldOps.Modules.WorkOrders.Domain;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Modules.WorkOrders.Data;

// internal, replacing InMemoryWorkOrderDirectory (Day 40) as
// IWorkOrderDirectory's real implementation — every state-machine rule
// (Day 41-46) is unchanged, just persisted via SaveChanges() instead of
// living only in a List<WorkOrder>. Still deliberately synchronous.
internal class EfWorkOrderDirectory : IWorkOrderDirectory
{
    private readonly WorkOrdersDbContext _dbContext;

    public EfWorkOrderDirectory(WorkOrdersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IReadOnlyList<WorkOrderSummary> GetAll()
    {
        return _dbContext.WorkOrders.Select(ToSummary).ToList();
    }

    public WorkOrderSummary? GetById(int id)
    {
        var workOrder = _dbContext.WorkOrders.FirstOrDefault(w => w.Id == id);
        return workOrder is null ? null : ToSummary(workOrder);
    }

    public WorkOrderSummary Create(string title, int organizationId, int? customerId = null)
    {
        var workOrder = new WorkOrder(title, organizationId, WorkOrderStatus.Open) { CustomerId = customerId };
        _dbContext.WorkOrders.Add(workOrder);
        _dbContext.SaveChanges();
        return ToSummary(workOrder);
    }

    public WorkOrderSummary? Assign(int workOrderId, int employeeId)
    {
        var workOrder = _dbContext.WorkOrders.FirstOrDefault(w => w.Id == workOrderId);
        if (workOrder is null || workOrder.Status != WorkOrderStatus.Open)
        {
            return null;
        }

        workOrder.Status = WorkOrderStatus.Assigned;
        workOrder.AssignedEmployeeId = employeeId;
        _dbContext.SaveChanges();
        return ToSummary(workOrder);
    }

    public WorkOrderSummary? Start(int workOrderId)
    {
        var workOrder = _dbContext.WorkOrders.FirstOrDefault(w => w.Id == workOrderId);
        if (workOrder is null || workOrder.Status != WorkOrderStatus.Assigned)
        {
            return null;
        }

        workOrder.Status = WorkOrderStatus.InProgress;
        _dbContext.SaveChanges();
        return ToSummary(workOrder);
    }

    public WorkOrderSummary? Complete(int workOrderId)
    {
        var workOrder = _dbContext.WorkOrders.FirstOrDefault(w => w.Id == workOrderId);
        if (workOrder is null || workOrder.Status != WorkOrderStatus.InProgress)
        {
            return null;
        }

        workOrder.Status = WorkOrderStatus.Completed;
        _dbContext.SaveChanges();
        return ToSummary(workOrder);
    }

    public WorkOrderSummary? Reassign(int workOrderId, int newEmployeeId)
    {
        var workOrder = _dbContext.WorkOrders.FirstOrDefault(w => w.Id == workOrderId);
        if (workOrder is null || (workOrder.Status != WorkOrderStatus.Assigned && workOrder.Status != WorkOrderStatus.InProgress))
        {
            return null;
        }

        workOrder.AssignedEmployeeId = newEmployeeId;
        _dbContext.SaveChanges();
        return ToSummary(workOrder);
    }

    public WorkOrderSummary? Unassign(int workOrderId)
    {
        var workOrder = _dbContext.WorkOrders.FirstOrDefault(w => w.Id == workOrderId);
        if (workOrder is null || (workOrder.Status != WorkOrderStatus.Assigned && workOrder.Status != WorkOrderStatus.InProgress))
        {
            return null;
        }

        workOrder.Status = WorkOrderStatus.Open;
        workOrder.AssignedEmployeeId = null;
        _dbContext.SaveChanges();
        return ToSummary(workOrder);
    }

    public WorkOrderSummary? Reopen(int workOrderId)
    {
        var workOrder = _dbContext.WorkOrders.FirstOrDefault(w => w.Id == workOrderId);
        if (workOrder is null || workOrder.Status != WorkOrderStatus.Completed)
        {
            return null;
        }

        workOrder.Status = WorkOrderStatus.InProgress;
        _dbContext.SaveChanges();
        return ToSummary(workOrder);
    }

    public WorkOrderSummary? AddEvidence(int workOrderId, string note)
    {
        var workOrder = _dbContext.WorkOrders.FirstOrDefault(w => w.Id == workOrderId);
        if (workOrder is null || workOrder.Status == WorkOrderStatus.Open)
        {
            return null;
        }

        workOrder.EvidenceNotes.Add(note);
        _dbContext.SaveChanges();
        return ToSummary(workOrder);
    }

    public WorkOrderSummary? Approve(int workOrderId)
    {
        var workOrder = _dbContext.WorkOrders.FirstOrDefault(w => w.Id == workOrderId);
        if (workOrder is null || workOrder.Status != WorkOrderStatus.Completed)
        {
            return null;
        }

        workOrder.CustomerApproved = true;
        _dbContext.SaveChanges();
        return ToSummary(workOrder);
    }

    private static WorkOrderSummary ToSummary(WorkOrder workOrder) =>
        new(workOrder.Id, workOrder.Title, workOrder.OrganizationId, workOrder.Status, workOrder.AssignedEmployeeId, workOrder.EvidenceNotes.AsReadOnly(), workOrder.CustomerId, workOrder.CustomerApproved);
}
