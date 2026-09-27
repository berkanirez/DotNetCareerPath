namespace FieldOps.Modules.WorkOrders.Domain;

// Day 73: the Inbox pattern's own table — the consumer-side mirror of
// OutboxMessage. Records "this consumer has already handled this exact
// message" so a redelivered message (RabbitMQ's own "at-least-once, never
// exactly-once" delivery guarantee) can be recognized and skipped instead
// of reprocessed. ConsumerName is part of the identity deliberately: two
// different consumers ("notifications", "audit") each need their OWN
// record of what THEY have processed — one consumer already having seen a
// message must never stop a different, independent consumer from
// processing its own first copy of it.
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
