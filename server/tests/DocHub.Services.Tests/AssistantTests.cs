using DocHub.Integrations.Knowledge;
using DocHub.Services.Chat;
using DocHub.Services.ViewModels;

namespace DocHub.Services.Tests;

/// <summary>
/// The assistant end to end against real Postgres, real blob storage and the
/// real retrieval path, with only the model scripted.
///
/// What is under test is the orchestrator's judgement — what it retrieves,
/// whether it calls the model at all, and what it does with a fabricated
/// citation. None of that depends on the model being good, and a real one
/// would only make these tests slow and flaky.
/// </summary>
[Collection(nameof(StackCollection))]
public sealed class AssistantTests(StackFixture fixture)
{
    private static string Unique(string name) => $"{name}-{Guid.NewGuid():N}"[..22];

    private const string VpnGuide = """
        ## Connecting to the VPN

        All staff working outside the office must connect through the company VPN before
        reaching internal systems. The gateway address is vpn.example-corp.internal and
        the client is downloaded from the IT portal.
        """;

    /// <summary>Mirrors and indexes a document, returning the folder it landed in.</summary>
    private static async Task<Guid> IndexAsync(StackFixture.Scope scope, string body, string name)
    {
        var document = await scope.PublishIndexedAsync($"{Unique(name)}/{name}.md", body);
        return document.FolderId;
    }

    /// <summary>Drains the answer stream into the events a caller would see.</summary>
    private static async Task<(List<ChatEvent> Events, string Answer)> AskAsync(
        StackFixture.Scope scope,
        AskRequest request)
    {
        var events = new List<ChatEvent>();
        var answer = new System.Text.StringBuilder();

        await foreach (var @event in scope.Chat.AskAsync(request))
        {
            events.Add(@event);
            if (@event is ChatEvent.Token token) answer.Append(token.Text);
        }

        return (events, answer.ToString().Trim());
    }

    [Fact]
    public async Task An_answer_is_grounded_in_the_retrieved_passages()
    {
        await using var scope = fixture.NewScope();
        var folderId = await IndexAsync(scope, VpnGuide, "vpn");

        scope.Llm.Answer = "Connect through the company VPN [1].";

        var (events, answer) = await AskAsync(scope, new AskRequest
        {
            Question = "How do I reach internal systems?",
            FolderId = folderId,
        });

        Assert.Contains("company VPN", answer);

        // The prompt must actually carry the document's text — a prompt without
        // it would produce an answer that only looks grounded.
        Assert.Contains("vpn.example-corp.internal", scope.Llm.LastPrompt);

        var completed = Assert.IsType<ChatEvent.Completed>(events[^1]);
        Assert.False(completed.IsRefusal);
        Assert.Single(completed.Citations);
    }

    [Fact]
    public async Task An_answer_given_without_a_source_names_the_one_that_was_missed()
    {
        // A second source that is configured and simply cannot be reached —
        // what an unreachable MCP server looks like from here.
        var broken = new UnreachableSource();

        await using var scope = fixture.NewScope(broken);
        var folderId = await IndexAsync(scope, VpnGuide, "degraded");

        scope.Llm.Answer = "Connect through the company VPN [1].";

        var (events, _) = await AskAsync(scope, new AskRequest
        {
            Question = "How do I reach internal systems?",
            FolderId = folderId,
        });

        var completed = Assert.IsType<ChatEvent.Completed>(events[^1]);

        // The documents still answered — one source failing must not lose an
        // answer the others could give.
        Assert.False(completed.IsRefusal);
        Assert.NotEmpty(completed.Citations);

        // And the reader is told it was answered on less than usual. Without
        // this a thinner answer is indistinguishable from a complete one.
        var degradation = Assert.Single(completed.Degradations);
        Assert.Contains("could not be searched", degradation);

        // Persisted, not just streamed: reopening the conversation tomorrow has
        // to say the same thing, exactly as it still shows what was cited.
        var transcript = await scope.Chat.GetTranscriptAsync(
            Assert.IsType<ChatEvent.SessionOpened>(events[0]).SessionId);

        var answer = transcript.Messages.Last(message => message.Role == "assistant");
        Assert.Equal(completed.Degradations, answer.Degradations);
    }

    [Fact]
    public async Task An_answer_from_healthy_sources_reports_no_degradation()
    {
        await using var scope = fixture.NewScope();
        var folderId = await IndexAsync(scope, VpnGuide, "healthy");

        scope.Llm.Answer = "Connect through the company VPN [1].";

        var (events, _) = await AskAsync(scope, new AskRequest
        {
            Question = "How do I reach internal systems?",
            FolderId = folderId,
        });

        // The warning has to be absent when nothing went wrong, or it stops
        // meaning anything when it does appear.
        Assert.Empty(Assert.IsType<ChatEvent.Completed>(events[^1]).Degradations);
    }

    [Fact]
    public async Task An_answer_that_cites_nothing_is_refused_rather_than_shown()
    {
        await using var scope = fixture.NewScope();
        var folderId = await IndexAsync(scope, VpnGuide, "ungrounded");

        // What a model does when the passages do not answer the question and it
        // would rather be helpful: a fluent, plausible answer out of its own
        // training, citing nothing because there was nothing to cite.
        scope.Llm.Answer =
            "To reverse a number in Python, convert it to a string and slice it with [::-1].";

        var (events, answer) = await AskAsync(scope, new AskRequest
        {
            Question = "How do I reverse a number in Python?",
            FolderId = folderId,
        });

        var completed = Assert.IsType<ChatEvent.Completed>(events[^1]);

        // Refused. Retrieval was not empty, so the guard before generation
        // could not catch this — an uncitable answer is the only evidence left
        // that the model answered from somewhere other than the passages.
        Assert.True(completed.IsRefusal);
        Assert.Empty(completed.Citations);
        Assert.DoesNotContain("[::-1]", completed.Content);

        // And the reader is not left looking at the fabrication that streamed
        // before we knew: the completion carries what was actually stored.
        Assert.Contains("don't have information", completed.Content);
        Assert.Contains("reverse", answer, StringComparison.OrdinalIgnoreCase);

        var transcript = await scope.Chat.GetTranscriptAsync(
            Assert.IsType<ChatEvent.SessionOpened>(events[0]).SessionId);

        var stored = transcript.Messages.Last(message => message.Role == "assistant");
        Assert.True(stored.IsRefusal);
        Assert.DoesNotContain("[::-1]", stored.Content);
    }

    [Fact]
    public async Task An_answer_that_decorates_a_fabrication_with_real_markers_is_refused()
    {
        await using var scope = fixture.NewScope();
        var folderId = await IndexAsync(scope, VpnGuide, "decorated");

        // The failure that got past the previous guard: the model invents an
        // answer and hangs a marker off each sentence. Every marker resolves —
        // the passages were genuinely supplied — so counting citations proves
        // nothing. What is missing is any connection between the sentences and
        // what those passages say.
        scope.Llm.Answer =
            "To make orange juice, peel the oranges and squeeze them [1]. Strain the juice "
            + "through a fine-mesh sieve to remove the pulp [1].";

        var (events, _) = await AskAsync(scope, new AskRequest
        {
            Question = "How do I make orange juice?",
            FolderId = folderId,
        });

        var completed = Assert.IsType<ChatEvent.Completed>(events[^1]);

        Assert.True(completed.IsRefusal);
        Assert.Empty(completed.Citations);
        Assert.DoesNotContain("sieve", completed.Content);
    }

    [Fact]
    public async Task An_answer_that_cites_one_real_passage_still_stands()
    {
        await using var scope = fixture.NewScope();
        var folderId = await IndexAsync(scope, VpnGuide, "grounded");

        scope.Llm.Answer = "Connect through the company VPN [1].";

        var (_, answer) = await AskAsync(scope, new AskRequest
        {
            Question = "How do I reach internal systems?",
            FolderId = folderId,
        });

        // The refusal rule must not swallow a properly grounded answer, which
        // is the whole risk of making it strict.
        Assert.Contains("company VPN", answer);
    }

    [Fact]
    public async Task A_document_counts_the_answers_that_cite_it()
    {
        await using var scope = fixture.NewScope();

        var document = await scope.PublishIndexedAsync($"{Unique("cited")}/vpn.md", VpnGuide);

        // A second document in a folder of its own, so it is never retrieved.
        var other = await scope.PublishIndexedAsync(
            $"{Unique("uncited")}/expenses.md", "## Expenses\n\nClaim within 30 days.");

        Assert.Equal(0, (await scope.Documents.GetAsync(document.Id)).CitedInAnswers);

        scope.Llm.Answer = "Connect through the company VPN [1].";

        foreach (var _ in Enumerable.Range(0, 2))
        {
            await AskAsync(scope, new AskRequest
            {
                Question = "How do I reach internal systems?",
                FolderId = document.FolderId,
            });
        }

        // Two answers cited it, so it says two — the count is of answers, not
        // of citations, and not of conversations.
        Assert.Equal(2, (await scope.Documents.GetAsync(document.Id)).CitedInAnswers);

        // And a document nobody's question reached stays at zero, which is what
        // makes the number worth showing at all.
        Assert.Equal(0, (await scope.Documents.GetAsync(other.Id)).CitedInAnswers);
    }

    [Fact]
    public async Task A_refusal_does_not_count_towards_any_document()
    {
        await using var scope = fixture.NewScope();

        var document = await scope.PublishIndexedAsync($"{Unique("refuse")}/vpn.md", VpnGuide);

        // A folder with nothing indexed in it retrieves nothing, so the
        // assistant declines without ever calling the model — and a declined
        // answer cites nobody.
        var empty = await scope.EmptyFolderAsync(Unique("empty"));

        var (events, _) = await AskAsync(scope, new AskRequest
        {
            Question = "How do I reach internal systems?",
            FolderId = empty,
        });

        Assert.True(Assert.IsType<ChatEvent.Completed>(events[^1]).IsRefusal);
        Assert.Equal(0, (await scope.Documents.GetAsync(document.Id)).CitedInAnswers);
    }

    /// <summary>A source that is configured but cannot be reached.</summary>
    private sealed class UnreachableSource : IKnowledgeSource
    {
        public string Name => "repositories";

        public string DisplayName => "Repositories";

        public string Description => "A source that exists only inside this test.";

        public Task<KnowledgeSourceStatus> CheckStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new KnowledgeSourceStatus(
                KnowledgeSourceState.Unavailable, "Scripted failure."));

        public Task<KnowledgeSearchResult> SearchAsync(
            KnowledgeQuery query,
            CancellationToken ct = default) =>
            throw new HttpRequestException("connection refused");
    }

    [Fact]
    public async Task Sources_are_announced_before_the_first_token()
    {
        await using var scope = fixture.NewScope();
        var folderId = await IndexAsync(scope, VpnGuide, "order");

        var (events, _) = await AskAsync(scope, new AskRequest
        {
            Question = "How do I reach internal systems?",
            FolderId = folderId,
        });

        var sourcesAt = events.FindIndex(e => e is ChatEvent.SourcesRetrieved);
        var firstTokenAt = events.FindIndex(e => e is ChatEvent.Token);

        // The UI shows what the answer is grounded on while it is still being
        // written; that only works if the sources arrive first.
        Assert.True(sourcesAt >= 0 && sourcesAt < firstTokenAt);
    }

    [Fact]
    public async Task A_question_with_no_matching_documents_never_reaches_the_model()
    {
        await using var scope = fixture.NewScope();

        // Nothing indexed here: retrieval finds nothing to ground an answer in.
        var folder = await scope.EmptyFolderAsync(Unique("Bare"));

        var (events, answer) = await AskAsync(scope, new AskRequest
        {
            Question = "What is the escalation path for a sev-one incident?",
            FolderId = folder,
        });

        // Asking a model to answer with no sources is exactly the situation
        // that produces confident fabrication, so it is not asked.
        Assert.Equal(0, scope.Llm.CallCount);

        var completed = Assert.IsType<ChatEvent.Completed>(events[^1]);
        Assert.True(completed.IsRefusal);
        Assert.Empty(completed.Citations);
        Assert.Contains("don't have information", answer);
    }

    [Fact]
    public async Task A_fabricated_citation_is_dropped_from_the_answer()
    {
        await using var scope = fixture.NewScope();
        var folderId = await IndexAsync(scope, VpnGuide, "fab");

        // The model cites a source it was never given.
        scope.Llm.Answer = "Connect through the VPN [1]. Rotate keys every 90 days [9].";

        var (events, answer) = await AskAsync(scope, new AskRequest
        {
            Question = "How do I reach internal systems?",
            FolderId = folderId,
        });

        var completed = Assert.IsType<ChatEvent.Completed>(events[^1]);

        Assert.Single(completed.Citations);
        Assert.Equal(1, completed.Citations[0].Marker);

        // The claim survives; the citation that cannot be checked does not.
        var stored = await scope.Chat.GetTranscriptAsync(SessionIdOf(events));
        var assistant = stored.Messages.Last();

        Assert.DoesNotContain("[9]", assistant.Content);
        Assert.Contains("[1]", assistant.Content);
        Assert.Contains("Rotate keys", assistant.Content);
    }

    [Fact]
    public async Task A_refusal_is_persisted_as_a_refusal()
    {
        await using var scope = fixture.NewScope();
        var folderId = await IndexAsync(scope, VpnGuide, "refuse");

        scope.Llm.Answer = GroundedPrompt.RefusalPhrase;

        var (events, _) = await AskAsync(scope, new AskRequest
        {
            Question = "How many vacation days do employees get?",
            FolderId = folderId,
        });

        var transcript = await scope.Chat.GetTranscriptAsync(SessionIdOf(events));
        var assistant = transcript.Messages.Last();

        // Recorded explicitly rather than inferred from empty citations: the
        // client renders a designed "I don't know" very differently from an
        // answer that merely failed to cite.
        Assert.True(assistant.IsRefusal);
        Assert.Empty(assistant.Citations);
    }

    [Fact]
    public async Task The_whole_turn_is_persisted_with_its_citations()
    {
        await using var scope = fixture.NewScope();
        var folderId = await IndexAsync(scope, VpnGuide, "persist");

        scope.Llm.Answer = "Connect through the company VPN [1].";

        var (events, _) = await AskAsync(scope, new AskRequest
        {
            Question = "How do I reach internal systems?",
            FolderId = folderId,
        });

        var transcript = await scope.Chat.GetTranscriptAsync(SessionIdOf(events));

        Assert.Equal(2, transcript.Messages.Count);
        Assert.Equal("user", transcript.Messages[0].Role);
        Assert.Equal("assistant", transcript.Messages[1].Role);

        // Citations round-trip through jsonb with their document reference
        // intact — that is what makes a historical answer still checkable.
        var citation = Assert.Single(transcript.Messages[1].Citations);
        Assert.Equal(1, citation.Marker);
        Assert.False(string.IsNullOrWhiteSpace(citation.Heading));
        Assert.NotEqual(Guid.Empty, citation.DocumentId);
    }

    [Fact]
    public async Task A_follow_up_continues_the_same_conversation_with_prior_turns()
    {
        await using var scope = fixture.NewScope();
        var folderId = await IndexAsync(scope, VpnGuide, "follow");

        var (first, _) = await AskAsync(scope, new AskRequest
        {
            Question = "How do I reach internal systems?",
            FolderId = folderId,
        });

        var sessionId = SessionIdOf(first);

        await AskAsync(scope, new AskRequest
        {
            Question = "And what is the gateway address?",
            SessionId = sessionId,
            FolderId = folderId,
        });

        var transcript = await scope.Chat.GetTranscriptAsync(sessionId);
        Assert.Equal(4, transcript.Messages.Count);

        // The follow-up must see the earlier turn, or "and what about…" has
        // nothing to resolve against.
        Assert.Contains(
            scope.Llm.LastMessages,
            message => message.Content.Contains("How do I reach internal systems?"));
    }

    [Fact]
    public async Task A_model_failure_is_reported_without_saving_a_broken_answer()
    {
        await using var scope = fixture.NewScope();
        var folderId = await IndexAsync(scope, VpnGuide, "fail");

        scope.Llm.Failure = new InvalidOperationException("model exploded");

        var (events, _) = await AskAsync(scope, new AskRequest
        {
            Question = "How do I reach internal systems?",
            FolderId = folderId,
        });

        var failed = Assert.IsType<ChatEvent.Failed>(events[^1]);
        Assert.Contains("unavailable", failed.Reason);

        // The question is kept so a retry has context, but no answer is stored
        // — a retry must not inherit a half-written one.
        var transcript = await scope.Chat.GetTranscriptAsync(SessionIdOf(events));
        Assert.Single(transcript.Messages);
        Assert.Equal("user", transcript.Messages[0].Role);
    }

    [Fact]
    public async Task An_empty_question_is_rejected()
    {
        await using var scope = fixture.NewScope();

        await Assert.ThrowsAsync<ValidationException>(
            async () => await AskAsync(scope, new AskRequest { Question = "   " }));
    }

    [Fact]
    public async Task Deleting_a_conversation_removes_its_transcript()
    {
        await using var scope = fixture.NewScope();
        var folderId = await IndexAsync(scope, VpnGuide, "del");

        var (events, _) = await AskAsync(scope, new AskRequest
        {
            Question = "How do I reach internal systems?",
            FolderId = folderId,
        });

        var sessionId = SessionIdOf(events);
        await scope.Chat.DeleteSessionAsync(sessionId);

        await Assert.ThrowsAsync<NotFoundException>(
            () => scope.Chat.GetTranscriptAsync(sessionId));
    }

    private static Guid SessionIdOf(IReadOnlyList<ChatEvent> events) =>
        events.OfType<ChatEvent.SessionOpened>().First().SessionId;
}
