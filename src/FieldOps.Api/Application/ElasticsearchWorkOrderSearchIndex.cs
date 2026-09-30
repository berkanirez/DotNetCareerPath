using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Polly;
using Polly.CircuitBreaker;

namespace FieldOps.Api.Application;

// Day 79: today's only IWorkOrderSearchIndex implementation. SQL Server
// stays the single source of truth (via IWorkOrderDirectory) — this class
// only ever mirrors a work order INTO a search-optimized copy; it is never
// read from to answer anything other than a search query.
public class ElasticsearchWorkOrderSearchIndex : IWorkOrderSearchIndex
{
    private const string IndexName = "workorders";

    private readonly ElasticsearchClient _client;

    // Day 87: built ONCE and held for this instance's whole lifetime (this
    // class is registered Singleton in Program.cs) — the circuit breaker's
    // failure count and open/closed state genuinely need to persist across
    // calls; a pipeline rebuilt per call would forget every past failure
    // and never actually open.
    //
    // Order matters: CircuitBreaker is added FIRST, so it is the OUTERMOST
    // strategy — it's checked before anything else runs, so when the
    // circuit is open, a call fails INSTANTLY without even attempting a
    // connection. Timeout is added SECOND, so it's INNERMOST, bounding only
    // the individual attempts the circuit breaker actually lets through.
    private readonly ResiliencePipeline _resiliencePipeline = new ResiliencePipelineBuilder()
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            // Day 86 live-caught the exact problem this solves: with
            // Elasticsearch down, every attempt still paid its full
            // connect-timeout cost, tick after tick. Two failures within a
            // 10-second window are enough to trip the breaker — no need to
            // keep proving what's already been proven twice.
            FailureRatio = 1.0,
            MinimumThroughput = 2,
            SamplingDuration = TimeSpan.FromSeconds(10),
            BreakDuration = TimeSpan.FromSeconds(15),
        })
        .AddTimeout(TimeSpan.FromSeconds(2))
        .Build();

    public ElasticsearchWorkOrderSearchIndex(ElasticsearchClient client)
    {
        _client = client;
    }

    public async Task IndexAsync(WorkOrderSearchDocument document, CancellationToken cancellationToken)
    {
        await _resiliencePipeline.ExecuteAsync(async ct =>
        {
            // A plain "index" call: creates the document if it's new, or
            // overwrites it wholesale if that Id already exists — exactly the
            // "upsert the whole document" shape a denormalized search copy needs,
            // never a partial field update.
            var response = await _client.IndexAsync(document, request => request.Index(IndexName).Id(document.Id), ct);

            // Day 80, live-caught: unlike RabbitMQ.Client (which throws on a
            // real connection failure), this client does NOT throw when
            // Elasticsearch is unreachable — it returns a response with
            // IsValidResponse == false instead. OutboxPublisher's retry logic
            // only works because it catches an EXCEPTION; without this check,
            // every failed attempt looked like a success and got marked
            // published anyway, permanently losing the indexing request.
            // This thrown exception is also exactly what the circuit
            // breaker above counts as a "failure."
            if (!response.IsValidResponse)
            {
                throw new InvalidOperationException($"Failed to index work order {document.Id}: {response.DebugInformation}");
            }
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkOrderSearchDocument>> SearchAsync(int organizationId, string query, CancellationToken cancellationToken)
    {
        var response = await _resiliencePipeline.ExecuteAsync(async ct =>
        {
            var searchResponse = await _client.SearchAsync<WorkOrderSearchDocument>(request => request
                .Indices(IndexName)
                .Query(q => q
                    .Bool(b => b
                        // Term, not Match: OrganizationId is an exact tenant
                        // filter (Day 40's isolation rule), never a fuzzy
                        // full-text match — this is what keeps one
                        // organization's search from ever surfacing another's data.
                        .Filter(f => f.Term(t => t.Field(d => d.OrganizationId).Value(organizationId)))
                        // Match, not Term: Title IS the full-text search — word
                        // matching, relevance-ranked, tolerant of extra words.
                        .Must(m => m.Match(mm => mm.Field(d => d.Title).Query(query)))
                    )
                ), ct);

            // Same reasoning as IndexAsync above — without this check, an
            // unreachable Elasticsearch would silently look identical to "zero
            // matches," which is a wrong, misleading answer, not just a missing one.
            if (!searchResponse.IsValidResponse)
            {
                throw new InvalidOperationException($"Search failed for organization {organizationId}: {searchResponse.DebugInformation}");
            }

            return searchResponse;
        }, cancellationToken);

        return response.Documents.ToList();
    }

    public async Task EnsureIndexExistsAsync(CancellationToken cancellationToken)
    {
        await _resiliencePipeline.ExecuteAsync(async ct =>
        {
            var existsResponse = await _client.Indices.ExistsAsync(IndexName, ct);
            if (existsResponse.Exists)
            {
                // Idempotent on purpose — safe to call every time the app
                // starts, whether this is the very first run ever or the
                // thousandth restart against an index that already exists.
                return;
            }

            var createResponse = await _client.Indices.CreateAsync(IndexName, request => request
                .Mappings(m => m
                    .Properties<WorkOrderSearchDocument>(p => p
                        .LongNumber(d => d.Id)
                        .LongNumber(d => d.OrganizationId)
                        .Text(d => d.Title)
                        // Keyword, not Text — unlike Title, Status is never
                        // full-text searched, only ever matched exactly (or not
                        // searched by at all today). Day 79's dynamic mapping
                        // guessed Text+keyword for this field too, purely
                        // because it's a string; this explicit mapping is the
                        // deliberate correction.
                        .Keyword(d => d.Status)
                    )
                ), ct);

            if (!createResponse.IsValidResponse)
            {
                throw new InvalidOperationException($"Failed to create the '{IndexName}' index: {createResponse.DebugInformation}");
            }
        }, cancellationToken);
    }

    public async Task RebuildOrganizationIndexAsync(int organizationId, IReadOnlyList<WorkOrderSearchDocument> documents, CancellationToken cancellationToken)
    {
        // Delete-by-query, filtered to THIS organization only — every other
        // organization's documents in the same index are left untouched.
        await _resiliencePipeline.ExecuteAsync(async ct =>
        {
            var deleteResponse = await _client.DeleteByQueryAsync<WorkOrderSearchDocument>(IndexName, request => request
                .Query(q => q.Term(t => t.Field(d => d.OrganizationId).Value(organizationId))), ct);

            if (!deleteResponse.IsValidResponse)
            {
                throw new InvalidOperationException($"Failed to clear organization {organizationId}'s documents before rebuilding: {deleteResponse.DebugInformation}");
            }
        }, cancellationToken);

        // Then re-add every one of this organization's real, current work
        // orders (read from SQL Server by the caller) — reusing the exact
        // same IndexAsync (and its own resilience pipeline) as every other
        // write to this index.
        foreach (var document in documents)
        {
            await IndexAsync(document, cancellationToken);
        }
    }
}
