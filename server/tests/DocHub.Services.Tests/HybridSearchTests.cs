using DocHub.Services.ViewModels;

namespace DocHub.Services.Tests;

/// <summary>
/// Search against real Postgres, so the tsvector column, the GIN index and the
/// pgvector distance operator are all genuinely exercised.
///
/// The embedding provider is the deterministic hashing one, which only knows
/// lexical overlap. That is enough to prove fusion, filtering and ranking
/// mechanics — it is deliberately not used to assert semantic quality, which
/// belongs to the model rather than to this code.
/// </summary>
[Collection(nameof(StackCollection))]
public sealed class HybridSearchTests(StackFixture fixture)
{
    private static UploadRequest Upload(string body, string fileName) =>
        new(StackFixture.FileOf(body), fileName, "text/markdown",
            System.Text.Encoding.UTF8.GetByteCount(body));

    private static string Unique(string name) => $"{name}-{Guid.NewGuid():N}"[..22];

    /// <summary>Uploads and fully indexes a document, returning its id.</summary>
    private static async Task<Guid> IndexAsync(
        StackFixture.Scope scope,
        Guid folderId,
        string fileName,
        string body)
    {
        var created = await scope.Documents.UploadAsync(folderId, Upload(body, fileName));
        await scope.Ingestion.IngestAsync(created.Id);
        return created.Id;
    }

    [Fact]
    public async Task An_exact_term_is_found_by_the_keyword_branch()
    {
        await using var scope = fixture.NewScope();
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("K")));

        var id = await IndexAsync(scope, folder.Id, "vpn.md", """
            ## Connection problems

            If the connection is refused your account may not yet be enrolled. Raise a ticket
            with the service desk and quote error code GP1102 so they can find the enrolment
            record quickly.
            """);

        var response = await scope.Search.SearchAsync(
            new SearchRequest { Query = "GP1102", FolderId = folder.Id });

        // An identifier is exactly what vector search has no reason to consider
        // close to anything — this is why the keyword branch exists.
        Assert.True(response.Diagnostics.KeywordMatches > 0);

        var hit = response.Results.First();
        Assert.Equal(id, hit.DocumentId);
        Assert.Contains(hit.MatchedBy, new[] { "keyword", "both" });
    }

    [Fact]
    public async Task A_chunk_both_branches_find_outranks_one_only_a_single_branch_found()
    {
        await using var scope = fixture.NewScope();
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("B")));

        await IndexAsync(scope, folder.Id, "printers.md", """
            ## Printer maintenance

            Printer maintenance covers replacing toner and clearing jams on every office
            printer. The printer maintenance schedule is published quarterly by facilities.
            """);

        await IndexAsync(scope, folder.Id, "unrelated.md", """
            ## Catering

            Catering requests for meetings go through the office manager with two days of
            notice, and the kitchen restocks milk and coffee every morning.
            """);

        // Scoped to this test's own folder: the fixture's database is shared
        // across the collection, so an unscoped ranking assertion would depend
        // on what every other test happened to index.
        var response = await scope.Search.SearchAsync(new SearchRequest
        {
            Query = "printer maintenance",
            FolderId = folder.Id,
        });

        Assert.NotEmpty(response.Results);

        // Reciprocal rank fusion adds a contribution per branch, so agreement
        // between them has to win.
        var top = response.Results.First();
        Assert.Equal("both", top.MatchedBy);
        Assert.All(
            response.Results.Skip(1),
            result => Assert.True(result.Score <= top.Score));
    }

    [Fact]
    public async Task Both_branches_run_on_one_request_scoped_context_without_colliding()
    {
        await using var scope = fixture.NewScope();
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("P")));

        await IndexAsync(scope, folder.Id, "backup.md", """
            ## Nightly backup

            The nightly backup runs at two in the morning and writes a full snapshot to the
            archive account before the retention job prunes anything older than ninety days.
            """);

        var response = await scope.Search.SearchAsync(new SearchRequest
        {
            Query = "nightly backup",
            FolderId = folder.Id,
        });

        // Both branches share the request's DbContext, which cannot serve two
        // commands at once. Running them concurrently silently loses the vector
        // half — and only when the embedding provider is fast enough for the
        // two queries to actually overlap, which is why it needs its own test.
        Assert.True(response.Diagnostics.VectorSearchAvailable,
            response.Diagnostics.VectorSearchError);
        Assert.True(response.Diagnostics.VectorMatches > 0, "the vector branch returned nothing");
        Assert.True(response.Diagnostics.KeywordMatches > 0, "the keyword branch returned nothing");
    }

    [Fact]
    public async Task Only_indexed_documents_are_searchable()
    {
        await using var scope = fixture.NewScope();
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("S")));

        // Uploaded but never ingested, so it is still Pending.
        await scope.Documents.UploadAsync(folder.Id, Upload("""
            ## Quarantine

            The quarantine procedure describes isolating a compromised workstation.
            """, "quarantine.md"));

        // Scoped to this folder, which holds nothing but the pending document.
        // Vector search has no relevance threshold — it returns the nearest
        // chunks whatever they are — so an unscoped query would pick up every
        // other test's documents and prove nothing.
        var response = await scope.Search.SearchAsync(new SearchRequest
        {
            Query = "quarantine procedure",
            FolderId = folder.Id,
        });

        // A document still in the pipeline must not be findable or citable.
        Assert.Empty(response.Results);
    }

    [Fact]
    public async Task A_failed_document_is_never_returned()
    {
        await using var scope = fixture.NewScope();
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("X")));

        var created = await scope.Documents.UploadAsync(
            folder.Id,
            new UploadRequest(StackFixture.FileOf("binary"), "poster.png", "image/png", 6));
        await scope.Ingestion.IngestAsync(created.Id);

        var response = await scope.Search.SearchAsync(new SearchRequest { Query = "poster" });

        Assert.DoesNotContain(response.Results, result => result.DocumentId == created.Id);
    }

    [Fact]
    public async Task Results_carry_the_chunk_position_a_citation_links_to()
    {
        await using var scope = fixture.NewScope();
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("C")));

        var id = await IndexAsync(scope, folder.Id, "runbook.md", """
            ## Escalation

            Escalate to the on-call engineer if the incident is still unresolved after thirty
            minutes, using the rota published in the operations channel.
            """);

        var response = await scope.Search.SearchAsync(new SearchRequest { Query = "escalate" });

        var hit = Assert.Single(response.Results, result => result.DocumentId == id);

        // The chunk id has to address a real chunk of that document, or the
        // /docs/:id?chunk=N link lands nowhere.
        var chunks = await scope.Chunks.GetForDocumentAsync(id);
        Assert.Contains(chunks, chunk => chunk.Ordinal == hit.ChunkId);

        Assert.False(string.IsNullOrWhiteSpace(hit.Heading));
        Assert.False(string.IsNullOrWhiteSpace(hit.Snippet));
    }

    [Fact]
    public async Task A_folder_filter_restricts_results_to_that_subtree()
    {
        await using var scope = fixture.NewScope();
        var engineering = await scope.Folders.CreateAsync(
            new CreateFolderRequest(null, Unique("Eng")));
        var operations = await scope.Folders.CreateAsync(
            new CreateFolderRequest(null, Unique("Ops")));

        const string Body = """
            ## Deployment window

            The deployment window is Thursday evening, after the nightly backup completes and
            before the reporting jobs start.
            """;

        var inEngineering = await IndexAsync(scope, engineering.Id, "deploy-eng.md", Body);
        var inOperations = await IndexAsync(scope, operations.Id, "deploy-ops.md", Body);

        var response = await scope.Search.SearchAsync(new SearchRequest
        {
            Query = "deployment window",
            FolderId = engineering.Id,
        });

        Assert.Contains(response.Results, result => result.DocumentId == inEngineering);
        Assert.DoesNotContain(response.Results, result => result.DocumentId == inOperations);
    }

    [Fact]
    public async Task An_extension_filter_excludes_other_file_types()
    {
        await using var scope = fixture.NewScope();
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("T")));

        const string Body = """
            ## Retention

            Retention policy keeps audit records for seven years and application logs for
            ninety days before they are purged automatically.
            """;

        var markdown = await IndexAsync(scope, folder.Id, "retention.md", Body);

        var response = await scope.Search.SearchAsync(new SearchRequest
        {
            Query = "retention policy",
            Extension = ["pdf"],
        });

        Assert.DoesNotContain(response.Results, result => result.DocumentId == markdown);
    }

    [Fact]
    public async Task An_empty_query_is_rejected_rather_than_returning_everything()
    {
        await using var scope = fixture.NewScope();

        await Assert.ThrowsAsync<ValidationException>(
            () => scope.Search.SearchAsync(new SearchRequest { Query = "   " }));
    }

    [Fact]
    public async Task Stop_words_are_not_returned_as_highlight_terms()
    {
        await using var scope = fixture.NewScope();
        var folder = await scope.Folders.CreateAsync(new CreateFolderRequest(null, Unique("H")));
        await IndexAsync(scope, folder.Id, "vpn.md", """
            ## Enrolment

            Contact the service desk to have your account enrolled before the first remote
            session, otherwise the gateway refuses the connection.
            """);

        var response = await scope.Search.SearchAsync(
            new SearchRequest { Query = "how do I contact the service desk" });

        // Highlighting "the" and "do" in every result is noise, not help.
        Assert.DoesNotContain("the", response.Terms);
        Assert.DoesNotContain("do", response.Terms);
        Assert.Contains("service", response.Terms);
    }
}
