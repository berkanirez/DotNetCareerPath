namespace FieldOps.Api.Application;

// Day 63: an AI provider abstraction — deliberately generic (a prompt in,
// a completion out), not "note summarizer" specific. The same shape as
// Day 51's INotificationSender: business code (WorkOrderNoteSummaryService)
// depends only on this interface, never on which real provider (or, today,
// fake) answers it.
public interface IAiProvider
{
    Task<string> SummarizeAsync(string prompt, CancellationToken cancellationToken);
}
