using DocHub.Services.ViewModels;

namespace DocHub.Services.Chat;

/// <summary>
/// Something that happened while answering, streamed to the client as it
/// happens.
///
/// Modelled as distinct events rather than a bare token stream because the
/// interesting parts of a grounded answer are not the tokens: which sources
/// were retrieved, whether the assistant declined, and which citations survived
/// verification.
/// </summary>
public abstract record ChatEvent
{
    /// <summary>The conversation this answer belongs to, sent first so a new session is addressable.</summary>
    public sealed record SessionOpened(Guid SessionId, string Title) : ChatEvent;

    /// <summary>
    /// What retrieval found, before generation starts. Sent early on purpose —
    /// seeing the sources while the answer is still being written is what makes
    /// the assistant's grounding legible rather than a claim.
    /// </summary>
    public sealed record SourcesRetrieved(IReadOnlyList<CitationViewModel> Sources) : ChatEvent;

    /// <summary>A fragment of the answer.</summary>
    public sealed record Token(string Text) : ChatEvent;

    /// <summary>The finished, persisted answer with its verified citations.</summary>
    public sealed record Completed(
        Guid MessageId,
        IReadOnlyList<CitationViewModel> Citations,
        bool IsRefusal) : ChatEvent;

    /// <summary>Generation failed. Distinct from a refusal, which is a valid answer.</summary>
    public sealed record Failed(string Reason) : ChatEvent;
}

/// <summary>
/// Answers questions from indexed documents only, with citations.
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Retrieves, generates and persists one turn, streaming progress as it
    /// goes. The turn is saved even when the assistant declines — a recorded
    /// "I don't know" is what makes the grounding auditable later.
    /// </summary>
    IAsyncEnumerable<ChatEvent> AskAsync(AskRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<ChatSessionViewModel>> ListSessionsAsync(CancellationToken ct = default);

    Task<ChatTranscriptViewModel> GetTranscriptAsync(Guid sessionId, CancellationToken ct = default);

    Task DeleteSessionAsync(Guid sessionId, CancellationToken ct = default);
}
