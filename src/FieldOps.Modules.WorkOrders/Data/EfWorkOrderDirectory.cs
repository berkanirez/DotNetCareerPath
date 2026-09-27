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

    public WorkOrderSummary? Complete(int workOrderId, string outboxEventType, string outboxPayload)
    {
        var workOrder = _dbContext.WorkOrders.FirstOrDefault(w => w.Id == workOrderId);
        if (workOrder is null || workOrder.Status != WorkOrderStatus.InProgress)
        {
            return null;
        }

        workOrder.Status = WorkOrderStatus.Completed;

        // Day 71: the Outbox pattern's whole point — this Add and the
        // Status change above are tracked by the SAME DbContext and
        // committed by the SAME SaveChanges call below, so either both
        // land or neither does. There is no window where the work order is
        // Completed in the database but no outbox row exists to eventually
        // get it published.
        _dbContext.OutboxMessages.Add(new OutboxMessage(outboxEventType, outboxPayload));

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

    public IReadOnlyList<OutboxMessageSummary> GetUnpublishedOutboxMessages()
    {
        return _dbContext.OutboxMessages
            .Where(m => m.PublishedAtUtc == null)
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => new OutboxMessageSummary(m.Id, m.EventType, m.Payload))
            .ToList();
    }

    public void MarkOutboxMessagePublished(int outboxMessageId)
    {
        var message = _dbContext.OutboxMessages.FirstOrDefault(m => m.Id == outboxMessageId);
        if (message is not null)
        {
            message.PublishedAtUtc = DateTime.UtcNow;
            _dbContext.SaveChanges();
        }
    }

    public bool HasProcessedMessage(string consumerName, string messageId)
    {
        return _dbContext.ProcessedMessages.Any(m => m.ConsumerName == consumerName && m.MessageId == messageId);
    }

    public void MarkMessageProcessed(string consumerName, string messageId)
    {
        try
        {
            _dbContext.ProcessedMessages.Add(new ProcessedMessage(consumerName, messageId));
            _dbContext.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // Day 19's exact two-layer pattern: HasProcessedMessage above
            // already covers the normal case; this catch is only a safety
            // net for the rare race where the same message is processed
            // concurrently — the database's own unique index (not this
            // code) is what actually decides which insert wins.
        }
    }

    public int RecordFailedAttempt(string consumerName, string messageId)
    {
        var attempt = _dbContext.FailedMessageAttempts
            .FirstOrDefault(m => m.ConsumerName == consumerName && m.MessageId == messageId);

        if (attempt is null)
        {
            attempt = new FailedMessageAttempt(consumerName, messageId);
            _dbContext.FailedMessageAttempts.Add(attempt);
        }

        attempt.AttemptCount++;
        _dbContext.SaveChanges();
        return attempt.AttemptCount;
    }

    private static WorkOrderSummary ToSummary(WorkOrder workOrder) =>
        new(workOrder.Id, workOrder.Title, workOrder.OrganizationId, workOrder.Status, workOrder.AssignedEmployeeId, workOrder.EvidenceNotes.AsReadOnly(), workOrder.CustomerId, workOrder.CustomerApproved);
}
