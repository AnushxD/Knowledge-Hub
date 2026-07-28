namespace DocHub.DataAccess.Entities;

/// <summary>Who produced a message.</summary>
public enum ChatRole
{
    User = 0,
    Assistant = 1,
}

/// <summary>
/// A source the assistant cited, denormalised onto the message.
///
/// The document title and heading are copied rather than joined: a citation
/// records what was said at the time. If the document is later renamed,
/// re-chunked or deleted, the historical answer should still show what it
/// actually cited instead of silently changing or losing it.
/// </summary>
/// <param name="Marker">
/// The bracketed number used in the answer text — <c>[1]</c>, <c>[2]</c>. This
/// is what ties a sentence to its source.
/// </param>
/// <param name="ChunkId">
/// Chunk position within the document, so the citation resolves to the exact
/// passage: <c>/docs/:documentId?chunk=:chunkId</c>.
/// </param>
public sealed record Citation(
    int Marker,
    Guid DocumentId,
    string DocumentTitle,
    int ChunkId,
    string Heading);

/// <summary>One turn in a conversation.</summary>
public class ChatMessage
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }

    public ChatRole Role { get; set; }

    public required string Content { get; set; }

    /// <summary>
    /// Sources backing this answer, empty for a user message. Stored as jsonb:
    /// citations are always read as a whole with their message and never
    /// queried across, so a child table would buy nothing.
    /// </summary>
    public IReadOnlyList<Citation> Citations { get; set; } = [];

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
