namespace FieldOps.Api.Application;

// Day 73: kept as its own small abstraction (not a direct dependency on
// IWorkOrderDirectory) so EventConsumerBase<TEvent> — deliberately generic,
// reusable for any future event type since Day 69 — never has to know that
// today's only backing store happens to live in the WorkOrders module.
public interface IInboxStore
{
    bool HasProcessed(string consumerName, string messageId);
    void MarkProcessed(string consumerName, string messageId);
}
