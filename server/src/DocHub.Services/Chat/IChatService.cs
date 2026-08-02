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
    /// <param name="Degradations">
    /// Sources that could not be searched for this answer, each one sentence.
    ///
    /// Carried on the answer rather than logged and forgotten: a source that
    /// failed is left out of one answer and has to be *named* in it, or a
    /// thinner answer is indistinguishable from a complete one. Empty is the
    /// normal case.
    /// </param>
    /// <param name="SourcesWithoutMatches">
    /// Sources that were searched for this answer and matched nothing, by
    /// display name.
    ///
    /// The counterpart to <paramref name="Degradations"/>, and deliberately not
    /// merged with it: a source that was asked and said "no" is working, while
    /// one that could not be asked is not, and only the second is a problem to
    /// fix. Both are reported for the same underlying reason — a source that
    /// contributed nothing is invisible otherwise, and silence reads as "never
    /// searched".
    /// </param>
    /// <param name="Content">
    /// The answer as it was persisted, which is not always what was streamed:
    /// unresolvable markers are stripped, and an answer that turned out to cite
    /// nothing is replaced by the refusal outright. The client shows this in
    /// place of what it accumulated, so the screen and the stored transcript
    /// cannot disagree.
    /// </param>
    public sealed record Completed(
        Guid MessageId,
        string Content,
        IReadOnlyList<CitationViewModel> Citations,
        bool IsRefusal,
        IReadOnlyList<string> Degradations,
        IReadOnlyList<string> SourcesWithoutMatches) : ChatEvent;

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
