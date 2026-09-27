namespace FieldOps.Modules.WorkOrders.Domain;

// Day 74: tracks how many times a given consumer has failed to process a
// given message — the mirror of ProcessedMessage, but for the "not
// succeeding yet" side. Once AttemptCount crosses a threshold, the message
// is moved to a dead-letter queue instead of being retried again.
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
