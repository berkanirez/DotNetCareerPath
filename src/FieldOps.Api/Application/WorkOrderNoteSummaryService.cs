namespace FieldOps.Api.Application;

// Day 63: the actual feature (summarizing a work order's evidence notes),
// kept separate from IAiProvider itself — IAiProvider stays generic
// ("prompt in, completion out"), this class is the one place that knows
// what a work order's evidence notes look like and how to turn them into a
// prompt worth asking about.
public class WorkOrderNoteSummaryService
{
    private readonly IAiProvider _aiProvider;

    public WorkOrderNoteSummaryService(IAiProvider aiProvider)
    {
        _aiProvider = aiProvider;
    }

    public Task<string> SummarizeAsync(IReadOnlyList<string> evidenceNotes, CancellationToken cancellationToken)
    {
        if (evidenceNotes.Count == 0)
        {
            // No prompt is built and IAiProvider is never called — there is
            // nothing to summarize, so there is nothing to ask an AI about.
            return Task.FromResult("No evidence notes have been added to this work order yet.");
        }

        var numberedNotes = evidenceNotes.Select((note, index) => $"{index + 1}. {note}");
        var prompt = "Summarize the following field service evidence notes in one or two sentences:\n"
            + string.Join("\n", numberedNotes);

        return _aiProvider.SummarizeAsync(prompt, cancellationToken);
    }
}
