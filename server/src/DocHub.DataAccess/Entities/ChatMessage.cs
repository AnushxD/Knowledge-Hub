namespace DocHub.DataAccess.Entities;

/// <summary>Who produced a message.</summary>
public enum ChatRole
{
    User = 0,
    Assistant = 1,
}

/// <summary>
/// What a cited passage points at, and therefore how it resolves to a link.
///
/// Stored as text in the jsonb, for the same reason the column enums are:
/// a dump stays readable, and reordering the members cannot silently remap
/// answers that were already persisted.
/// </summary>
public enum CitationKind
{
    /// <summary>A document in this hub. Resolves to <c>/docs/:id?chunk=n</c>.</summary>
    Document = 0,

    /// <summary>
    /// Something outside the hub — a repository file reached over MCP. It has
    /// no document id, which is the entire reason this enum exists.
    /// </summary>
    External = 1,
}

/// <summary>
/// A source the assistant cited, denormalised onto the message.
///
/// The title and heading are copied rather than joined: a citation records what
/// was said at the time. If the document is later renamed, re-chunked or
/// deleted — or the external system moves the file — the historical answer
/// should still show what it actually cited instead of silently changing or
/// losing it.
///
/// The optional members are the price of one record covering both kinds, and
/// are preferred to a second table or a polymorphic jsonb payload: citations
/// are read as a whole with their message, so the simplest shape that
/// round-trips is the right one. <see cref="Kind"/> says which members are
/// meaningful.
/// </summary>
/// <param name="Marker">
/// The bracketed number used in the answer text — <c>[1]</c>, <c>[2]</c>. This
/// is what ties a sentence to its source.
/// </param>
/// <param name="Title">
/// Display name as it was at the time: a document title, or a repository file
/// path.
/// </param>
/// <param name="Heading">Where within it — a heading, a page, a line range.</param>
/// <param name="DocumentId">Set for <see cref="CitationKind.Document"/> only.</param>
/// <param name="ChunkId">
/// Chunk position within the document, so the citation resolves to the exact
/// passage: <c>/docs/:documentId?chunk=:chunkId</c>. Document citations only.
/// </param>
/// <param name="Url">
/// Where to send a reader for an external citation, when the source supplied a
/// link. Null is normal — a source that can name a passage but not link to it
/// still cites it honestly, and the UI renders the reference without a link
/// rather than inventing one.
/// </param>
/// <param name="SourceName">
/// Which knowledge source produced this, matching <c>IKnowledgeSource.Name</c>.
/// Recorded so an answer drawn from several sources can say which is which, and
/// so a source removed later does not make its past citations unattributable.
/// </param>
public sealed record Citation(
    int Marker,
    CitationKind Kind,
    string Title,
    string Heading,
    Guid? DocumentId = null,
    int? ChunkId = null,
    string? Url = null,
    string? SourceName = null);

/// <summary>One turn in a conversation.</summary>
public class ChatMessage
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }

    public ChatRole Role { get; set; }

    public required string Content { get; set; }

    /// <summary>
    /// Sources backing this answer, empty for a user message. Stored as jsonb:
    /// citations are read as a whole with their message, so a child table would
    /// buy nothing. The one query that crosses messages — how many answers cite
    /// a given document — is a containment test the GIN index on the column
    /// covers, which is cheaper than the join a child table would need.
    /// </summary>
    public IReadOnlyList<Citation> Citations { get; set; } = [];

    /// <summary>
    /// Knowledge sources that could not be searched for this answer, each as one
    /// sentence naming what was missed.
    ///
    /// Persisted rather than reported live and forgotten. An answer given while
    /// the repository source was unreachable was grounded in less than usual,
    /// and that is a property of the answer — reopening the conversation
    /// tomorrow should still say so, exactly as it still shows what was cited.
    ///
    /// Empty is the normal case and means every source answered.
    /// </summary>
    public IReadOnlyList<string> Degradations { get; set; } = [];

    /// <summary>
    /// Knowledge sources that were searched for this answer and matched nothing,
    /// by display name.
    ///
    /// Persisted for the same reason as <see cref="Degradations"/>, and kept
    /// separate from it for the opposite one: these sources worked. Which
    /// sources had nothing to say is a property of the answer — a reopened
    /// conversation that showed only the sources which contributed would leave a
    /// reader unable to tell a source that found nothing from one that was never
    /// consulted.
    ///
    /// Empty means every source contributed at least one passage.
    /// </summary>
    public IReadOnlyList<string> SourcesWithoutMatches { get; set; } = [];

    /// <summary>
    /// True when the assistant declined because retrieval found nothing to
    /// ground an answer in.
    ///
    /// Recorded explicitly rather than inferred from empty citations: "I don't
    /// know" is the correct, designed outcome for an unanswerable question, and
    /// the UI presents it very differently from a failure.
    /// </summary>
    public bool IsRefusal { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ChatSession? Session { get; set; }
}
