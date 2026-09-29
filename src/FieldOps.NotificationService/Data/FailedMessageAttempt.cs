namespace FieldOps.NotificationService.Data;

// Day 76: the mirror of ProcessedMessage — the notification service's own
// dead-letter/retry bookkeeping, per ADR 0005. Same shape as Day 74's
// FailedMessageAttempt in FieldOps.Api's WorkOrders module, duplicated
// (not shared) into this service's own database.
internal class FailedMessageAttempt
{
    public int Id { get; set; }
    public string ConsumerName { get; set; }
    public string MessageId { get; set; }
    public int AttemptCount { get; set; }

    public FailedMessageAttempt(string consumerName, string messageId)
    {
        ConsumerName = consumerName;
        MessageId = messageId;
        AttemptCount = 0;
    }
}
