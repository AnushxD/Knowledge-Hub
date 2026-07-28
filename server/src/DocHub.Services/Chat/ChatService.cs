using System.Runtime.CompilerServices;
using System.Text;
using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;
using DocHub.DataAccess.Repositories;
using DocHub.Integrations.Llm;
using DocHub.Services.Search;
using DocHub.Services.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocHub.Services.Chat;

internal sealed class ChatService(
    IChatRepository sessions,
    ISearchService search,
    ILlmProvider llm,
    ICurrentUser currentUser,
    IOptions<ChatOptions> options,
    ILogger<ChatService> logger) : IChatService
{
    private readonly ChatOptions options = options.Value;

    public async IAsyncEnumerable<ChatEvent> AskAsync(
        AskRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var question = request.Question?.Trim() ?? string.Empty;

        if (question.Length == 0)
            throw new ValidationException("Ask a question.");

        if (question.Length > options.MaxQuestionLength)
            throw new ValidationException(
                $"Questions are limited to {options.MaxQuestionLength} characters.");

        // ---- 1. Open or continue the conversation ---------------------------

        var session = request.SessionId is { } existingId
            ? await sessions.GetTranscriptAsync(existingId, ct)
                ?? throw new NotFoundException("Chat session", existingId)
            : null;

        var sessionId = session?.Session.Id ?? Guid.Empty;

        if (session is null)
        {
            var created = await sessions.CreateSessionAsync(
                currentUser.Id, TitleFrom(question), ct);

            sessionId = created.Id;
            yield return new ChatEvent.SessionOpened(created.Id, created.Title);
        }
        else
        {
            yield return new ChatEvent.SessionOpened(
                session.Session.Id, session.Session.Title);
        }

        await sessions.AppendMessageAsync(
            sessionId,
            new NewChatMessageDto { Role = ChatRole.User, Content = question },
            ct);

        // ---- 2. Retrieve ----------------------------------------------------

        var retrieval = await search.RetrieveAsync(
            new SearchRequest
            {
                Query = question,
                FolderId = request.FolderId,
                Take = options.PassageCount,
            },
            ct);

        var passages = retrieval.Passages;

        yield return new ChatEvent.SourcesRetrieved(
            [.. passages.Select((passage, index) => new CitationViewModel(
                index + 1,
                passage.DocumentId,
                passage.DocumentTitle,
                passage.ChunkId,
                passage.Heading))]);

        // Nothing retrieved means there is nothing to ground an answer in, so
        // the model is never called. Asking it to answer with no sources is
        // precisely the situation that produces confident fabrication.
        if (passages.Count == 0)
        {
            var refusal = await SaveRefusalAsync(sessionId, NoSourcesMessage(retrieval), ct);

            yield return new ChatEvent.Token(refusal.Content);
            yield return new ChatEvent.Completed(refusal.Id, [], IsRefusal: true);
            yield break;
        }

        // ---- 3. Generate ----------------------------------------------------

        var systemPrompt = GroundedPrompt.Build(passages);
        var history = BuildHistory(session, question);

        var answer = new StringBuilder();
        var failure = default(string);

        await foreach (var fragment in StreamSafelyAsync(systemPrompt, history, ct))
        {
            if (fragment.Error is { } error)
            {
                failure = error;
                break;
            }

            answer.Append(fragment.Text);
            yield return new ChatEvent.Token(fragment.Text!);
        }

        if (failure is not null)
        {
            // The user's question is already saved; the failed turn is not
            // persisted as an answer, so a retry does not inherit a broken one.
            yield return new ChatEvent.Failed(failure);
            yield break;
        }

        // ---- 4. Verify and persist -----------------------------------------

        var text = answer.ToString().Trim();

        if (text.Length == 0)
        {
            yield return new ChatEvent.Failed(
                "The model returned an empty answer. Try asking again.");
            yield break;
        }

        var isRefusal = GroundedPrompt.IsRefusal(text);

        // Every marker is checked against the sources actually supplied. A
        // model asked to cite will occasionally invent a plausible number, and
        // an uncheckable citation is worse than none — it makes the answer look
        // better supported than it is.
        var citations = isRefusal ? [] : GroundedPrompt.VerifyCitations(text, passages);
        var cleaned = isRefusal ? text : GroundedPrompt.StripUnresolvedMarkers(text, citations);

        if (!isRefusal && citations.Count == 0)
        {
            logger.LogWarning(
                "Answer in session {SessionId} cited nothing verifiable across {PassageCount} passages",
                sessionId, passages.Count);
        }

        var saved = await sessions.AppendMessageAsync(
            sessionId,
            new NewChatMessageDto
            {
                Role = ChatRole.Assistant,
                Content = cleaned,
                Citations = citations,
                IsRefusal = isRefusal,
            },
            ct)
            ?? throw new NotFoundException("Chat session", sessionId);

        logger.LogInformation(
            "Answered in session {SessionId} from {PassageCount} passages with {CitationCount} "
            + "citations (refusal: {IsRefusal}) using {Provider}",
            sessionId, passages.Count, citations.Count, isRefusal, llm.Name);

        yield return new ChatEvent.Completed(
            saved.Id,
            [.. citations.Select(ToViewModel)],
            isRefusal);
    }

    public async Task<IReadOnlyList<ChatSessionViewModel>> ListSessionsAsync(
        CancellationToken ct = default)
    {
        var list = await sessions.ListSessionsAsync(currentUser.Id, ct: ct);
        return [.. list.Select(ToViewModel)];
    }

    public async Task<ChatTranscriptViewModel> GetTranscriptAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        var transcript = await sessions.GetTranscriptAsync(sessionId, ct)
            ?? throw new NotFoundException("Chat session", sessionId);

        return new ChatTranscriptViewModel(
            ToViewModel(transcript.Session),
            [.. transcript.Messages.Select(ToViewModel)]);
    }

    public async Task DeleteSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (!await sessions.DeleteSessionAsync(sessionId, ct))
            throw new NotFoundException("Chat session", sessionId);

        logger.LogInformation("Deleted chat session {SessionId}", sessionId);
    }

    /// <summary>
    /// Wraps the provider stream so a mid-generation failure becomes a value
    /// rather than an exception — an iterator cannot both yield and catch
    /// around a yield, and the partial answer still has to reach the client.
    /// </summary>
    private async IAsyncEnumerable<(string? Text, string? Error)> StreamSafelyAsync(
        string systemPrompt,
        IReadOnlyList<LlmMessage> history,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var stream = llm.StreamAsync(systemPrompt, history, ct).GetAsyncEnumerator(ct);

        try
        {
            while (true)
            {
                string? fragment;
                string? error = null;

                // The advance is wrapped, not the yield: C# forbids yielding
                // from a catch block, so the failure is captured as a value
                // here and surfaced below.
                try
                {
                    if (!await stream.MoveNextAsync()) break;
                    fragment = stream.Current;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Generation failed using {Provider}", llm.Name);

                    fragment = null;
                    error = $"The assistant is unavailable ({exception.Message}).";
                }

                if (error is not null)
                {
                    yield return (null, error);
                    yield break;
                }

                yield return (fragment, null);
            }
        }
        finally
        {
            await stream.DisposeAsync();
        }
    }

    /// <summary>
    /// Prior turns plus the new question, oldest first.
    ///
    /// Trimmed to a recent window: the retrieved passages are the expensive part
    /// of the prompt, and an unbounded transcript would crowd them out of the
    /// model's context — the failure mode being an assistant that stops seeing
    /// its own sources.
    /// </summary>
    private IReadOnlyList<LlmMessage> BuildHistory(ChatTranscriptDto? session, string question)
    {
        var history = new List<LlmMessage>();

        if (session is not null)
        {
            var recent = session.Messages
                .TakeLast(options.HistoryTurns * 2)
                .Select(message => new LlmMessage(
                    message.Role == ChatRole.User ? LlmRole.User : LlmRole.Assistant,
                    message.Content));

            history.AddRange(recent);
        }

        history.Add(new LlmMessage(LlmRole.User, question));

        return history;
    }

    private async Task<ChatMessageDto> SaveRefusalAsync(
        Guid sessionId,
        string content,
        CancellationToken ct) =>
        await sessions.AppendMessageAsync(
            sessionId,
            new NewChatMessageDto
            {
                Role = ChatRole.Assistant,
                Content = content,
                IsRefusal = true,
            },
            ct)
            ?? throw new NotFoundException("Chat session", sessionId);

    /// <summary>
    /// Explains an empty retrieval in terms the user can act on. "Nothing
    /// matched" and "semantic matching is down" look identical from the
    /// outside, and only one of them means the answer does not exist.
    /// </summary>
    private static string NoSourcesMessage(RetrievalResult retrieval) =>
        retrieval.VectorSearchError is not null
            ? GroundedPrompt.RefusalPhrase
                + " Note that semantic matching was unavailable for this question, "
                + "so only exact keyword matches were searched."
            : GroundedPrompt.RefusalPhrase
                + " Nothing in the indexed documents matched this question. Only documents "
                + "that finished ingestion are searchable.";

    /// <summary>
    /// A session title from the opening question, so history reads as a list of
    /// questions rather than of timestamps.
    /// </summary>
    private static string TitleFrom(string question)
    {
        var collapsed = string.Join(' ', question.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return collapsed.Length <= 80 ? collapsed : collapsed[..77].TrimEnd() + "…";
    }

    private static CitationViewModel ToViewModel(Citation citation) =>
        new(
            citation.Marker,
            citation.DocumentId,
            citation.DocumentTitle,
            citation.ChunkId,
            citation.Heading);

    private static ChatSessionViewModel ToViewModel(ChatSessionDto session) =>
        new(session.Id, session.Title, session.MessageCount, session.CreatedAt, session.UpdatedAt);

    private static ChatMessageViewModel ToViewModel(ChatMessageDto message) =>
        new(
            message.Id,
            message.Role.ToString().ToLowerInvariant(),
            message.Content,
            [.. message.Citations.Select(ToViewModel)],
            message.IsRefusal,
            message.CreatedAt);
}
