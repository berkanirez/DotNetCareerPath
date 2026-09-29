using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;

namespace FieldOps.Api.Application;

// Day 79: today's only IWorkOrderSearchIndex implementation. SQL Server
// stays the single source of truth (via IWorkOrderDirectory) — this class
// only ever mirrors a work order INTO a search-optimized copy; it is never
// read from to answer anything other than a search query.
public class ElasticsearchWorkOrderSearchIndex : IWorkOrderSearchIndex
{
    private const string IndexName = "workorders";

    private readonly ElasticsearchClient _client;

    public ElasticsearchWorkOrderSearchIndex(ElasticsearchClient client)
    {
        _client = client;
    }

    public async Task IndexAsync(WorkOrderSearchDocument document, CancellationToken cancellationToken)
    {
        // A plain "index" call: creates the document if it's new, or
        // overwrites it wholesale if that Id already exists — exactly the
        // "upsert the whole document" shape a denormalized search copy needs,
        // never a partial field update.
        var response = await _client.IndexAsync(document, request => request.Index(IndexName).Id(document.Id), cancellationToken);

        // Day 80, live-caught: unlike RabbitMQ.Client (which throws on a
        // real connection failure), this client does NOT throw when
        // Elasticsearch is unreachable — it returns a response with
        // IsValidResponse == false instead. OutboxPublisher's retry logic
        // only works because it catches an EXCEPTION; without this check,
        // every failed attempt looked like a success and got marked
        // published anyway, permanently losing the indexing request.
        if (!response.IsValidResponse)
        {
            throw new InvalidOperationException($"Failed to index work order {document.Id}: {response.DebugInformation}");
        }
    }

    public async Task<IReadOnlyList<WorkOrderSearchDocument>> SearchAsync(int organizationId, string query, CancellationToken cancellationToken)
    {
        var response = await _client.SearchAsync<WorkOrderSearchDocument>(request => request
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
            ), cancellationToken);

        // Same reasoning as IndexAsync above — without this check, an
        // unreachable Elasticsearch would silently look identical to "zero
        // matches," which is a wrong, misleading answer, not just a missing one.
        if (!response.IsValidResponse)
        {
            throw new InvalidOperationException($"Search failed for organization {organizationId}: {response.DebugInformation}");
        }

        return response.Documents.ToList();
    }

    public async Task EnsureIndexExistsAsync(CancellationToken cancellationToken)
    {
        var existsResponse = await _client.Indices.ExistsAsync(IndexName, cancellationToken);
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
            ), cancellationToken);

        if (!createResponse.IsValidResponse)
        {
            throw new InvalidOperationException($"Failed to create the '{IndexName}' index: {createResponse.DebugInformation}");
        }
    }

    public async Task RebuildOrganizationIndexAsync(int organizationId, IReadOnlyList<WorkOrderSearchDocument> documents, CancellationToken cancellationToken)
    {
        // Delete-by-query, filtered to THIS organization only — every other
        // organization's documents in the same index are left untouched.
        var deleteResponse = await _client.DeleteByQueryAsync<WorkOrderSearchDocument>(IndexName, request => request
            .Query(q => q.Term(t => t.Field(d => d.OrganizationId).Value(organizationId))), cancellationToken);

        if (!deleteResponse.IsValidResponse)
        {
            throw new InvalidOperationException($"Failed to clear organization {organizationId}'s documents before rebuilding: {deleteResponse.DebugInformation}");
        }

        // Then re-add every one of this organization's real, current work
        // orders (read from SQL Server by the caller) — reusing the exact
        // same IndexAsync (and its own IsValidResponse check) as every other
        // write to this index.
        foreach (var document in documents)
        {
            await IndexAsync(document, cancellationToken);
        }
    }
}
