namespace FieldOps.NotificationService;

// Day 76: this service's own copy of FieldOps.Api's IInboxStore (Day 73) —
// same shape, same purpose, but EventConsumerBase below only ever needs to
// know THIS interface, never anything about how it's actually backed. Its
// only implementation here (NotificationServiceInboxStore) is backed by
// this service's own NotificationServiceDbContext, never IWorkOrderDirectory.
public interface IInboxStore
{
    bool HasProcessed(string consumerName, string messageId);
    void MarkProcessed(string consumerName, string messageId);
    int RecordFailedAttempt(string consumerName, string messageId);
}
