using FieldOps.Api.Application;

namespace FieldOps.Api.Tests;

// Day 63: same hand-written-fake reasoning as EmployeeApplicationServiceTests
// (Day 33) — IAiProvider is a single-method interface, so a real, minimal
// fake is simpler than a mock setup, and it also lets one of these tests
// prove a genuine behavioral guarantee (never called) that a mock's own
// setup/verify ceremony wouldn't make any clearer.
public class WorkOrderNoteSummaryServiceTests
{
    [Fact]
    public async Task SummarizeAsync_WithNotes_BuildsPromptFromNotesAndReturnsProviderResult()
    {
        var provider = new RecordingAiProvider();
        var service = new WorkOrderNoteSummaryService(provider);

        var summary = await service.SummarizeAsync(
            new List<string> { "Checked the pump", "Replaced the filter" },
            CancellationToken.None);

        Assert.Contains("Checked the pump", provider.ReceivedPrompt);
        Assert.Contains("Replaced the filter", provider.ReceivedPrompt);
        Assert.Equal($"[Recorded] {provider.ReceivedPrompt}", summary);
    }

    [Fact]
    public async Task SummarizeAsync_WithNoNotes_ReturnsPlaceholderWithoutCallingProvider()
    {
        var service = new WorkOrderNoteSummaryService(new ThrowingAiProvider());

        var summary = await service.SummarizeAsync(new List<string>(), CancellationToken.None);

        Assert.Equal("No evidence notes have been added to this work order yet.", summary);
    }

    // Records the exact prompt it was called with, so the test can assert on
    // what WorkOrderNoteSummaryService actually built — not just that some
    // string came back.
    private class RecordingAiProvider : IAiProvider
    {
        public string ReceivedPrompt { get; private set; } = string.Empty;

        public Task<string> SummarizeAsync(string prompt, CancellationToken cancellationToken)
        {
            ReceivedPrompt = prompt;
            return Task.FromResult($"[Recorded] {prompt}");
        }
    }

    // Throws if ever called — the only way to genuinely prove the "no notes"
    // branch short-circuits before reaching IAiProvider, rather than just
    // assuming it from reading the code.
    private class ThrowingAiProvider : IAiProvider
    {
        public Task<string> SummarizeAsync(string prompt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("IAiProvider must not be called when there are no evidence notes.");
    }
}
