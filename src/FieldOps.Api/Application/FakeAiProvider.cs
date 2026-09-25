namespace FieldOps.Api.Application;

// Day 63: today's only IAiProvider implementation, registered directly in
// Program.cs — exactly Day 51's LoggingNotificationSender shape (no real
// external channel yet, just a real interface with a working, deterministic
// stand-in behind it). Deterministic and offline on purpose: no API key, no
// network call, no per-run randomness — so both the manual /summary check
// today and WorkOrderNoteSummaryServiceTests tomorrow-and-beyond get the
// exact same answer for the exact same prompt, every time.
public class FakeAiProvider : IAiProvider
{
    public Task<string> SummarizeAsync(string prompt, CancellationToken cancellationToken)
    {
        return Task.FromResult($"[Fake AI summary] {prompt}");
    }
}
