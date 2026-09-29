namespace FieldOps.NotificationService.Data;

// Day 76: the notification service's OWN Inbox record, per ADR 0005 —
// deliberately the same shape as FieldOps.Api's WorkOrders-module
// ProcessedMessage (Day 73), but now living in this service's own
// database, with no shared table and no reference back into FieldOps.Api.
// This service only ever has ONE consumer ("notifications"), so
// ConsumerName is kept only for the same reason Day 73 kept it there: it
// costs nothing today and avoids a schema change the day a second consumer
// (e.g. a future SMS channel) is added to this same service.
internal class ProcessedMessage
{
    public int Id { get; set; }
    public string ConsumerName { get; set; }
    public string MessageId { get; set; }
    public DateTime ProcessedAtUtc { get; set; }

    public ProcessedMessage(string consumerName, string messageId)
    {
        ConsumerName = consumerName;
        MessageId = messageId;
        ProcessedAtUtc = DateTime.UtcNow;
    }
}
