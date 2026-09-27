using FieldOps.Modules.WorkOrders;

namespace FieldOps.Api.Application;

// Day 73: today's only IInboxStore implementation — a thin adapter over
// IWorkOrderDirectory's new inbox methods. Nothing stops a later,
// unrelated event type from getting its own IInboxStore backed by a
// different module's database; EventConsumerBase<TEvent> never needs to
// change either way.
public class WorkOrderInboxStore : IInboxStore
{
    private readonly IWorkOrderDirectory _workOrderDirectory;

    public WorkOrderInboxStore(IWorkOrderDirectory workOrderDirectory)
    {
        _workOrderDirectory = workOrderDirectory;
    }

    public bool HasProcessed(string consumerName, string messageId) =>
        _workOrderDirectory.HasProcessedMessage(consumerName, messageId);

    public void MarkProcessed(string consumerName, string messageId) =>
        _workOrderDirectory.MarkMessageProcessed(consumerName, messageId);
}
