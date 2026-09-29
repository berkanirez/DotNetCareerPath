using FieldOps.NotificationService.Data;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.NotificationService;

// Day 76: this service's only IInboxStore implementation — backed
// DIRECTLY by NotificationServiceDbContext, not by any directory/repository
// abstraction. Unlike WorkOrders (a module inside a larger host, guarded by
// ADR 0001/0002's internal-domain boundary), this whole service has no
// other module to hide its database from — there's nothing here for a
// directory interface to protect against. This is exactly the replacement
// ADR 0005 anticipated for FieldOps.Api's WorkOrderInboxStore, now backed
// by a genuinely separate database instead of IWorkOrderDirectory.
internal class NotificationServiceInboxStore : IInboxStore
{
    private readonly NotificationServiceDbContext _dbContext;

    public NotificationServiceInboxStore(NotificationServiceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public bool HasProcessed(string consumerName, string messageId)
    {
        return _dbContext.ProcessedMessages.Any(m => m.ConsumerName == consumerName && m.MessageId == messageId);
    }

    public void MarkProcessed(string consumerName, string messageId)
    {
        try
        {
            _dbContext.ProcessedMessages.Add(new ProcessedMessage(consumerName, messageId));
            _dbContext.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // Day 19/73's exact two-layer pattern: HasProcessed above
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
}
