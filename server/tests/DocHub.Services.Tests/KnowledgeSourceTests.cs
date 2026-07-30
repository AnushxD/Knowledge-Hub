using DocHub.DataAccess.Entities;
using DocHub.Integrations.Knowledge;
using DocHub.Services.Chat;
using DocHub.Services.Knowledge;
using DocHub.Services.Search;
using DocHub.Services.ViewModels;

namespace DocHub.Services.Tests;

/// <summary>
/// The fan-out across knowledge sources, against the real document source and
/// the real null repository source, with extra sources scripted per test.
///
/// What is under test is the composite's judgement: that one broken source
/// costs an answer some grounding rather than all of it, that two sources
/// agreeing on a passage do not spend two citations on it, and that a source
/// which is off by design never reads as a source which is broken.
/// </summary>
[Collection(nameof(StackCollection))]
public sealed class KnowledgeSourceTests(StackFixture fixture)
{
    private const string RunbookBody = """
        ## Restarting the ingestion worker

        Drain the queue before restarting the worker, then bring it back with the
        supervisor. Jobs already in flight finish; nothing is lost.
        """;

    private static string Unique(string name) => $"{name}-{Guid.NewGuid():N}"[..22];

    private static UploadRequest Upload(string body, string fileName) =>
        new(StackFixture.FileOf(body), fileName, "text/markdown",
            System.Text.Encoding.UTF8.GetByteCount(body));

    /// <summary>Uploads and indexes a document, returning the folder it landed in.</summary>
    private static async Task<Guid> IndexAsync(StackFixture.Scope scope, string name)
    {
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique(name)));
        var document = await scope.Documents.UploadAsync(folder.Id, Upload(RunbookBody, $"{name}.md"));
        await scope.Ingestion.IngestAsync(document.Id);
        return folder.Id;
    }

    private static SearchRequest Ask(Guid folderId) =>
        new() { Query = "How do I restart the ingestion worker?", FolderId = folderId, Take = 5 };

    private static KnowledgeResult ResultFrom(Guid documentId, int chunkId, string text) =>
        new(
            KnowledgeResultKind.Document,
            "Scripted source",
            "Section 1",
            text,
            1.0,
            "keyword",
            DocumentId: documentId,
            ChunkId: chunkId);

    [Fact]
    public async Task Every_source_is_searched_for_one_question()
    {
        var wiki = new FakeKnowledgeSource("wiki", []);
        var tickets = new FakeKnowledgeSource("tickets", []);

        await using var scope = fixture.NewScope(wiki, tickets);
        var folderId = await IndexAsync(scope, "fanout");

        await scope.Knowledge.RetrieveAsync(Ask(folderId));

        // The point of the abstraction is that adding a source adds it to every
        // question, with no change anywhere else.
        Assert.Equal(1, wiki.SearchCount);
        Assert.Equal(1, tickets.SearchCount);
    }

    [Fact]
    public async Task The_null_repository_source_contributes_nothing_but_is_still_listed()
    {
        await using var scope = fixture.NewScope();
        var folderId = await IndexAsync(scope, "nullsrc");

        var retrieval = await scope.Knowledge.RetrieveAsync(Ask(folderId));
        var sources = await scope.Knowledge.DescribeSourcesAsync();

        // Every passage came from the documents; the stub added none.
        Assert.NotEmpty(retrieval.Passages);
        Assert.Empty(retrieval.Degradations);

        var repositories = Assert.Single(sources, source => source.Name == "repositories");

        // Inactive, not unavailable: nothing is broken, and a source that is
        // permanently red is one users learn to ignore.
        Assert.Equal("inactive", repositories.State);
        Assert.Contains("documents only", repositories.Detail);

        var documents = Assert.Single(sources, source => source.Name == "documents");
        Assert.Equal("active", documents.State);
    }

    [Fact]
    public async Task Passages_from_a_second_source_are_merged_into_the_ranked_list()
    {
        var wiki = new FakeKnowledgeSource("wiki",
            [ResultFrom(Guid.CreateVersion7(), 0, "The worker is supervised by systemd.")]);

        await using var scope = fixture.NewScope(wiki);
        var folderId = await IndexAsync(scope, "merge");

        var retrieval = await scope.Knowledge.RetrieveAsync(Ask(folderId));

        Assert.Contains(retrieval.Passages, passage => passage.Text.Contains("Drain the queue"));
        Assert.Contains(retrieval.Passages, passage => passage.Text.Contains("systemd"));

        // Fused by rank, not by the sources' own scores: the scripted source
        // reports 1.0 and the document source reports a reciprocal-rank sum, and
        // comparing those numbers directly would be meaningless.
        Assert.All(retrieval.Passages, passage => Assert.True(passage.Score < 1.0));
    }

    [Fact]
    public async Task A_source_outside_the_hub_contributes_a_passage_with_no_document_id()
    {
        var repository = new FakeKnowledgeSource("repositories",
        [
            new KnowledgeResult(
                KnowledgeResultKind.External,
                "src/Worker/IngestionWorker.cs",
                "lines 40-58",
                "The worker is restarted by supervisor policy on a non-zero exit.",
                1.0,
                "keyword",
                Url: "https://git.example.org/hub/blob/abc123/src/Worker/IngestionWorker.cs#L40-L58",
                // Its own stable ordinal — not a document chunk. Deduplication
                // keys on it, so a source without ordinals must still vary it.
                ChunkId: 4071),
        ]);

        await using var scope = fixture.NewScope(repository);
        var folderId = await IndexAsync(scope, "external");

        var retrieval = await scope.Knowledge.RetrieveAsync(Ask(folderId));

        var passage = Assert.Single(
            retrieval.Passages,
            candidate => candidate.Kind == PassageKind.External);

        // The whole point of widening the model: a citation that resolves
        // somewhere other than /docs/:id, with nothing pretending to be a
        // document id.
        Assert.Null(passage.DocumentId);
        Assert.Equal("src/Worker/IngestionWorker.cs", passage.Title);
        Assert.StartsWith("https://git.example.org/", passage.Url);

        // Attributed by the composite, so a source cannot claim another's name.
        Assert.Equal("repositories", passage.SourceName);

        // Documents still come back alongside it — adding a source adds to the
        // grounding rather than replacing it.
        Assert.Contains(retrieval.Passages, candidate => candidate.Kind == PassageKind.Document);
    }

    [Fact]
    public async Task An_external_passage_is_cited_without_being_mistaken_for_a_document()
    {
        var repository = new FakeKnowledgeSource("repositories",
        [
            new KnowledgeResult(
                KnowledgeResultKind.External,
                "README.md",
                "Getting started",
                "Run docker compose up before starting the API.",
                1.0,
                "keyword",
                ChunkId: 7),
        ]);

        await using var scope = fixture.NewScope(repository);
        var folderId = await IndexAsync(scope, "externalcite");

        var retrieval = await scope.Knowledge.RetrieveAsync(Ask(folderId));

        // The marker is the passage's 1-based position, exactly as the prompt
        // numbers them — so the citation this resolves to is the one the model
        // would have been pointing at.
        var marker = retrieval.Passages
            .Select((passage, index) => (passage, index))
            .First(entry => entry.passage.Kind == PassageKind.External)
            .index + 1;

        var citations = GroundedPrompt.VerifyCitations($"Start it first [{marker}].", retrieval.Passages);

        var citation = Assert.Single(citations);
        Assert.Equal(CitationKind.External, citation.Kind);
        Assert.Null(citation.DocumentId);
        // Null rather than 0: an external passage's ordinal is a dedupe key, and
        // persisting it as a chunk would invite the UI to build a /docs link.
        Assert.Null(citation.ChunkId);
        Assert.Equal("README.md", citation.Title);
    }

    [Fact]
    public async Task The_same_passage_from_two_sources_is_offered_once()
    {
        await using var probe = fixture.NewScope();
        var folderId = await IndexAsync(probe, "dedupe");

        var first = await probe.Knowledge.RetrieveAsync(Ask(folderId));
        var original = first.Passages[0];

        // A second source that indexes the same file and returns the same chunk.
        var mirror = new FakeKnowledgeSource("mirror",
            [ResultFrom(original.DocumentId!.Value, original.ChunkId, original.Text)]);

        await using var scope = fixture.NewScope(mirror);
        var retrieval = await scope.Knowledge.RetrieveAsync(Ask(folderId));

        // One passage, not two: duplicates would spend two citation slots on one
        // source and make the answer look better supported than it is.
        Assert.Single(
            retrieval.Passages,
            passage => passage.DocumentId == original.DocumentId
                && passage.ChunkId == original.ChunkId);
    }

    [Fact]
    public async Task A_failing_source_degrades_the_answer_instead_of_losing_it()
    {
        var broken = new FakeKnowledgeSource("wiki", [],
            searchFailure: new HttpRequestException("connection refused"));

        await using var scope = fixture.NewScope(broken);
        var folderId = await IndexAsync(scope, "degrade");

        var retrieval = await scope.Knowledge.RetrieveAsync(Ask(folderId));

        // The documents still ground the answer.
        Assert.NotEmpty(retrieval.Passages);

        // But the thinner grounding is reported rather than hidden — a thin
        // answer and a broken source must not look the same.
        var degradation = Assert.Single(retrieval.Degradations);
        Assert.Contains("could not be searched", degradation);
        Assert.Contains("connection refused", degradation);
    }

    [Fact]
    public async Task An_answer_is_still_produced_when_a_source_is_down()
    {
        var broken = new FakeKnowledgeSource("wiki", [],
            searchFailure: new HttpRequestException("connection refused"));

        await using var scope = fixture.NewScope(broken);
        var folderId = await IndexAsync(scope, "stillans");

        scope.Llm.Answer = "Drain the queue first [1].";

        var events = new List<ChatEvent>();
        await foreach (var @event in scope.Chat.AskAsync(new AskRequest
        {
            Question = "How do I restart the ingestion worker?",
            FolderId = folderId,
        }))
        {
            events.Add(@event);
        }

        var completed = Assert.IsType<ChatEvent.Completed>(events[^1]);

        // One unreachable source must not turn a question the documents could
        // answer into an error.
        Assert.False(completed.IsRefusal);
        Assert.Single(completed.Citations);
    }

    [Fact]
    public async Task A_refusal_names_the_source_that_could_not_be_searched()
    {
        var broken = new FakeKnowledgeSource("wiki", [],
            searchFailure: new HttpRequestException("connection refused"));

        await using var scope = fixture.NewScope(broken);

        // An empty folder, so the documents contribute nothing either.
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("Bare")));

        var answer = new System.Text.StringBuilder();
        await foreach (var @event in scope.Chat.AskAsync(new AskRequest
        {
            Question = "How do I restart the ingestion worker?",
            FolderId = folder.Id,
        }))
        {
            if (@event is ChatEvent.Token token) answer.Append(token.Text);
        }

        // Still a refusal — with nothing retrieved the model is never called —
        // but the user is told the answer may exist somewhere unreachable
        // rather than being told it does not exist.
        Assert.Equal(0, scope.Llm.CallCount);
        Assert.Contains("don't have information", answer.ToString());
        Assert.Contains("could not be searched", answer.ToString());
    }

    [Fact]
    public async Task A_source_that_cannot_report_its_status_is_shown_as_unavailable()
    {
        var mute = new FakeKnowledgeSource("wiki", [],
            statusFailure: new HttpRequestException("no route to host"));

        await using var scope = fixture.NewScope(mute);

        var sources = await scope.Knowledge.DescribeSourcesAsync();
        var wiki = Assert.Single(sources, source => source.Name == "wiki");

        Assert.Equal("unavailable", wiki.State);
        Assert.Contains("no route to host", wiki.Detail);

        // One silent source must not blank the whole screen.
        Assert.Equal("active", Assert.Single(sources, s => s.Name == "documents").State);
    }

    [Fact]
    public async Task An_empty_query_is_rejected_rather_than_reported_as_a_broken_source()
    {
        var wiki = new FakeKnowledgeSource("wiki", []);
        await using var scope = fixture.NewScope(wiki);

        // A bad request is the caller's fault and applies to every source, so it
        // must not be swallowed into "this source is unwell".
        await Assert.ThrowsAsync<ValidationException>(
            () => scope.Knowledge.RetrieveAsync(new SearchRequest { Query = "  " }));

        Assert.Equal(0, wiki.SearchCount);
    }

    [Fact]
    public async Task A_source_that_never_replies_is_left_out_rather_than_stalling_the_answer()
    {
        // Longer than any patience, so only the deadline can end it.
        var hung = new FakeKnowledgeSource("wiki", [], hangFor: TimeSpan.FromMinutes(5));

        await using var scope = fixture.NewScope(
            new KnowledgeOptions { SourceTimeoutSeconds = 1 }, hung);

        var folderId = await IndexAsync(scope, "hung");

        var started = System.Diagnostics.Stopwatch.StartNew();
        var retrieval = await scope.Knowledge.RetrieveAsync(Ask(folderId));
        started.Stop();

        // The whole point: failure isolation already covered a source that
        // throws. A source that simply never answers used to hold up the
        // fan-out, because Task.WhenAll waits for every one of them.
        Assert.True(
            started.Elapsed < TimeSpan.FromSeconds(20),
            $"the fan-out waited {started.Elapsed.TotalSeconds:F1}s on a hung source");

        Assert.NotEmpty(retrieval.Passages);

        var degradation = Assert.Single(retrieval.Degradations);
        Assert.Contains("did not respond", degradation);
    }

    [Fact]
    public async Task A_hung_source_shows_as_unavailable_rather_than_hanging_the_screen()
    {
        var hung = new FakeKnowledgeSource("wiki", [], hangFor: TimeSpan.FromMinutes(5));

        await using var scope = fixture.NewScope(
            new KnowledgeOptions { SourceTimeoutSeconds = 1 }, hung);

        var sources = await scope.Knowledge.DescribeSourcesAsync();
        var wiki = Assert.Single(sources, source => source.Name == "wiki");

        // A screen whose job is to report that a source is unreachable must not
        // itself hang on that source.
        Assert.Equal("unavailable", wiki.State);
        Assert.Contains("did not respond", wiki.Detail);

        Assert.Equal("active", Assert.Single(sources, s => s.Name == "documents").State);
    }

    [Fact]
    public async Task A_caller_who_gives_up_cancels_the_whole_request()
    {
        var hung = new FakeKnowledgeSource("wiki", [], hangFor: TimeSpan.FromMinutes(5));

        await using var scope = fixture.NewScope(
            new KnowledgeOptions { SourceTimeoutSeconds = 60 }, hung);

        var folderId = await IndexAsync(scope, "cancelled");

        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        // The deadline and the caller's token are different things, and the
        // composite has to tell them apart: a source that ran out of time is
        // left out, but a client that went away wants the request abandoned —
        // not an answer assembled for nobody.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scope.Knowledge.RetrieveAsync(Ask(folderId), caller.Token));
    }

    /// <summary>
    /// A source that returns, or fails, exactly as the test tells it to.
    ///
    /// The behaviour under test is the composite's — what it asks, what it does
    /// with a failure, how it merges — none of which needs a source that really
    /// searches anything.
    /// </summary>
    private sealed class FakeKnowledgeSource(
        string name,
        IReadOnlyList<KnowledgeResult> results,
        Exception? searchFailure = null,
        Exception? statusFailure = null,
        TimeSpan? hangFor = null) : IKnowledgeSource
    {
        public int SearchCount { get; private set; }

        public string Name => name;

        public string DisplayName => name;

        public string Description => "A source that exists only inside this test.";

        public async Task<KnowledgeSourceStatus> CheckStatusAsync(CancellationToken ct = default)
        {
            if (statusFailure is not null) throw statusFailure;

            // Honours the token, as a real HTTP client would — a source that
            // ignored cancellation entirely could not be rescued by any
            // deadline the caller sets.
            if (hangFor is { } wait) await Task.Delay(wait, ct);

            return new KnowledgeSourceStatus(KnowledgeSourceState.Active, "Scripted.");
        }

        public async Task<KnowledgeSearchResult> SearchAsync(
            KnowledgeQuery query,
            CancellationToken ct = default)
        {
            SearchCount++;

            if (searchFailure is not null) throw searchFailure;

            if (hangFor is { } wait) await Task.Delay(wait, ct);

            return new KnowledgeSearchResult(results);
        }
    }
}
