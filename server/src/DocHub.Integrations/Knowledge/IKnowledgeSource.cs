namespace DocHub.Integrations.Knowledge;

/// <summary>Whether a source is contributing to answers — and if not, why not.</summary>
public enum KnowledgeSourceState
{
    /// <summary>Connected, and searched on every question.</summary>
    Active,

    /// <summary>
    /// Deliberately not wired up. Contributes nothing, and that is not a fault:
    /// a stub standing in for a system this deployment does not have must not
    /// read as an outage.
    /// </summary>
    Inactive,

    /// <summary>Configured, but not answering right now.</summary>
    Unavailable,
}

/// <summary>A source's readiness, as shown on the sources screen.</summary>
/// <param name="Detail">
/// One sentence a user can act on, naming the fix when there is one. "Not
/// configured" and "configured but unreachable" look identical from the
/// outside, and only one of them is a problem.
/// </param>
public sealed record KnowledgeSourceStatus(KnowledgeSourceState State, string Detail);

/// <summary>A question put to a source, independent of how that source searches.</summary>
/// <param name="FolderId">
/// Restricts the search to a folder subtree. Sources with no notion of folders
/// ignore it rather than returning nothing — a document-shaped filter must not
/// silently switch a repository source off.
/// </param>
/// <param name="Take">How many passages the caller can use.</param>
/// <param name="Text">The question as it was asked.</param>
/// <param name="SemanticText">
/// The same question with the conversation's subject in front of it, when the
/// question was too thin to stand alone — "can you specify the paths?" becomes
/// "how do I get Activity Analytics? can you specify the paths?".
///
/// Null when the question already carried its own subject, which is most of
/// them. A source matching on meaning should prefer this; one matching on
/// literal words should not, since every word added is one more it will insist
/// on finding.
/// </param>
public sealed record KnowledgeQuery(
    string Text,
    Guid? FolderId,
    int Take,
    string? SemanticText = null);

/// <summary>
/// What a passage points at, and therefore how a citation to it resolves.
///
/// The document case carries an id into our own store; the external case
/// carries at most a URL. Keeping them one type rather than two means the
/// orchestrator, the prompt builder and the citation verifier stay unaware of
/// the difference — only rendering cares.
/// </summary>
public enum KnowledgeResultKind
{
    /// <summary>A document in this hub, addressable by id and chunk.</summary>
    Document = 0,

    /// <summary>Something outside the hub, such as a repository file.</summary>
    External = 1,
}

/// <summary>
/// One passage a source offers as grounding.
///
/// No longer document-shaped: a repository source cites a file at a commit,
/// which has no document id and no chunk ordinal. <see cref="Kind"/> says which
/// of the optional members mean anything.
/// </summary>
/// <param name="Title">
/// What to call this in an answer — a document title, or a repository file path.
/// </param>
/// <param name="Heading">Where within it: a heading, a page, a line range.</param>
/// <param name="Text">The passage in full, not a display snippet.</param>
/// <param name="Score">
/// The source's own relevance score. Comparable within a source and meaningless
/// across sources, so callers merging several sources must fuse on rank.
/// </param>
/// <param name="MatchedBy">How this was found — "keyword", "vector", "both".</param>
/// <param name="DocumentId">Set for <see cref="KnowledgeResultKind.Document"/> only.</param>
/// <param name="ChunkId">
/// Chunk ordinal for a document. For an external passage this still has to be
/// *something* stable, because deduplication and citation identity key on it —
/// a source without ordinals should pass a hash of its own locator rather than
/// zero for everything, or two different files will look like one passage.
/// </param>
/// <param name="Url">
/// Where a reader can go, when the source can say. Null is a normal answer, not
/// a gap: the citation still names the passage, and the UI shows it without a
/// link rather than fabricating one.
/// </param>
public sealed record KnowledgeResult(
    KnowledgeResultKind Kind,
    string Title,
    string Heading,
    string Text,
    double Score,
    string MatchedBy,
    Guid? DocumentId = null,
    int ChunkId = 0,
    string? Url = null);

/// <summary>What one source returned for one query.</summary>
/// <param name="Results">Best first. Empty is a normal answer, not a failure.</param>
/// <param name="Degradation">
/// Set when the source answered, but with less than it should have — a branch
/// that was down, a partial index. The caller still uses the results, and the
/// user is told the grounding is thinner than usual rather than being left to
/// wonder why the answer is thin.
/// </param>
public sealed record KnowledgeSearchResult(
    IReadOnlyList<KnowledgeResult> Results,
    string? Degradation = null)
{
    public static readonly KnowledgeSearchResult Empty = new([]);
}

/// <summary>
/// One body of knowledge the assistant can ground an answer in.
///
/// This is the seam that lets repository and code search join document search
/// without the RAG orchestrator changing: the orchestrator fans out over these
/// and merges, and neither knows nor cares that one of them is a Postgres query
/// and another an MCP call over the network.
///
/// The interface lives in Integrations because that is the only layer both an
/// external client and the Service layer can see — Services references
/// Integrations, never the reverse, so an MCP client implementing this could
/// not reach a contract defined in Services. Implementations sit wherever their
/// work belongs: an external system's client here, a wrapper over our own
/// search in Services.
/// </summary>
public interface IKnowledgeSource
{
    /// <summary>Stable identifier used in logs and configuration. Never localised.</summary>
    string Name { get; }

    /// <summary>Short label for the sources screen.</summary>
    string DisplayName { get; }

    /// <summary>One line on what this source contributes to an answer.</summary>
    string Description { get; }

    /// <summary>
    /// Whether this source can answer right now. Separate from
    /// <see cref="SearchAsync"/> so the sources screen can be honest about an
    /// idle source without putting a question to it.
    /// </summary>
    Task<KnowledgeSourceStatus> CheckStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the passages this source considers most relevant.
    ///
    /// Implementations report trouble by throwing: the caller isolates each
    /// source so one failure degrades the answer instead of losing it, and
    /// swallowing the exception here would hide the failure from that handling.
    /// </summary>
    Task<KnowledgeSearchResult> SearchAsync(KnowledgeQuery query, CancellationToken ct = default);
}
