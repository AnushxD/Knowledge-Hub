using DocHub.Integrations.Knowledge;
using DocHub.Services.Search;
using DocHub.Services.ViewModels;

namespace DocHub.Services.Knowledge;

/// <summary>
/// The hub's own documents, presented as one knowledge source among several.
///
/// Lives in Services, not Integrations, despite implementing an interface
/// defined there: it calls no external system, it wraps
/// <see cref="ISearchService"/>. Putting it in Integrations would mean
/// Integrations referencing Services, which inverts the layering.
///
/// It adds no ranking of its own. Hybrid search and reciprocal rank fusion
/// already happened inside <see cref="ISearchService.RetrieveAsync"/>, which is
/// the same call the search screen makes — so the assistant can still only cite
/// what a user searching for the same thing would have been shown.
/// </summary>
internal sealed class DocumentKnowledgeSource(ISearchService search) : IKnowledgeSource
{
    public string Name => "documents";

    public string DisplayName => "Documents";

    public string Description =>
        "Everything uploaded to the hub, searched by keyword and by meaning together.";

    /// <summary>
    /// Always active: these documents are the hub's reason to exist, and there
    /// is no configuration that switches them off. Whether the database behind
    /// them is reachable is the readiness check's job, not this screen's.
    /// </summary>
    public Task<KnowledgeSourceStatus> CheckStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new KnowledgeSourceStatus(
            KnowledgeSourceState.Active,
            "Searched on every question. Only documents that finished ingestion are "
            + "retrievable — anything still processing or failed is neither searchable nor "
            + "citable."));

    public async Task<KnowledgeSearchResult> SearchAsync(
        KnowledgeQuery query,
        CancellationToken ct = default)
    {
        var retrieval = await search.RetrieveAsync(
            new SearchRequest
            {
                Query = query.Text,
                FolderId = query.FolderId,
                Take = query.Take,
            },
            ct);

        return new KnowledgeSearchResult(
            [.. retrieval.Passages.Select(passage => new KnowledgeResult(
                KnowledgeResultKind.Document,
                passage.Title,
                passage.Heading,
                passage.Text,
                passage.Score,
                passage.MatchedBy,
                DocumentId: passage.DocumentId,
                ChunkId: passage.ChunkId))],
            // A vector branch that is down is a degradation, not a failure: the
            // keyword half is still worth answering from, and the user is told
            // the grounding is thinner than usual.
            retrieval.VectorSearchError);
    }
}
