using DocHub.DataAccess;
using DocHub.DataAccess.Entities;
using DocHub.DataAccess.Repositories;
using DocHub.Integrations.Embeddings;
using DocHub.Integrations.Knowledge;
using DocHub.Integrations.Llm;
using DocHub.Integrations.SourceControl;
using DocHub.Services;
using DocHub.Services.Activity;
using DocHub.Services.Chat;
using DocHub.Services.Documents;
using DocHub.Services.Folders;
using DocHub.Services.Ingestion;
using DocHub.Services.Ingestion.Extraction;
using DocHub.Services.Knowledge;
using DocHub.Services.Repository;
using DocHub.Services.Search;
using DocHub.Services.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocHub.Services.Tests;

/// <summary>
/// Wires the real Service layer to the real repositories against real Postgres
/// — the whole stack minus HTTP and minus GitLab.
///
/// Mocking the repositories here would only prove the mocks behave; the
/// interesting bugs live in the seams between layers, such as whether a
/// directory leaving the repository actually takes its documents' chunks with
/// it. GitLab itself is the one thing faked, by an in-memory tree that hashes
/// its own content — see <see cref="FakeSourceRepository"/>.
/// </summary>
public sealed class StackFixture : IAsyncLifetime
{
    private const string DefaultDb =
        "Host=localhost;Port=5432;Database=documenthub_services_test;Username=documenthub;Password=documenthub_local_dev";

    private string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("DOCHUB_TEST_DB") ?? DefaultDb;

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
    }

    private DocHubDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DocHubDbContext>()
            // Mirrors AddDataAccess: without this Npgsql has no mapping for the
            // pgvector column and the model fails validation.
            .UseNpgsql(ConnectionString, npgsql => npgsql.UseVector())
            .Options);

    /// <summary>The services of one request, sharing a DbContext as a request would.</summary>
    /// <param name="extraSources">
    /// Knowledge sources registered alongside the real two. Lets a test add a
    /// source that fails, or that returns passages nothing else knows about,
    /// without a second fixture.
    /// </param>
    public Scope NewScope(params IKnowledgeSource[] extraSources) =>
        NewScope(new KnowledgeOptions(), extraSources);

    /// <param name="knowledgeOptions">
    /// Lets a test shorten the per-source deadline so a hung source can be
    /// proven to degrade without the test itself waiting ten seconds.
    /// </param>
    public Scope NewScope(
        KnowledgeOptions knowledgeOptions,
        params IKnowledgeSource[] extraSources)
    {
        var db = CreateContext();
        var folderRepo = new FolderRepository(db);
        var documentRepo = new DocumentRepository(db);
        var chunkRepo = new ChunkRepository(db);
        var chatRepo = new ChatRepository(db);
        var syncStateRepo = new RepositorySyncStateRepository(db);
        var user = new TestCurrentUser();
        var queue = new RecordingIngestionQueue();
        var repository = new FakeSourceRepository();
        var gitLabOptions = Options.Create(new GitLabOptions
        {
            BaseUrl = "https://gitlab.test",
            ProjectPath = repository.ProjectPath,
            Branch = repository.Branch,
            WebhookSecret = "test-secret",
        });

        var ingestionOptions = Options.Create(new IngestionOptions());

        // Hashing embeddings, not Ollama: tests must not depend on a model
        // being pulled, and the pipeline's wiring is what is under test here,
        // not the quality of the vectors.
        var embeddings = new HashingEmbeddingProvider(
            Options.Create(new EmbeddingOptions
            {
                Provider = EmbeddingOptions.HashingProvider,
                Dimensions = DocHubDbContext.EmbeddingDimensions,
            }));

        var extractors = new TextExtractorRegistry(
            [new PlainTextExtractor(), new PdfTextExtractor(), new OpenXmlTextExtractor()]);

        // The relevance floor is switched off here, not tuned down: the hashing
        // embedding provider produces deterministic vectors with no semantic
        // geometry, so "distance" between a question and its own document is
        // arbitrary. A floor would reject everything and every test would be
        // measuring the floor rather than what it meant to. The floor itself is
        // tested where it lives, against real vectors, in the DataAccess suite.
        var searchService = new SearchService(
            chunkRepo,
            embeddings,
            Options.Create(new KnowledgeOptions
            {
                SourceTimeoutSeconds = knowledgeOptions.SourceTimeoutSeconds,
                MaxPassageDistance = double.MaxValue,
            }),
            NullLogger<SearchService>.Instance);

        // Tests reconcile overrides against configuration themselves, so the
        // fixture hands out the repository rather than a settings reader bound
        // to one set of options.
        var settingRepo = new RepositorySourceSettingRepository(db);

        // The real settings reader over the real table, not a stub: what an
        // administrator saves has to reach the sync and the webhook, and a
        // fixed reader here would let that wiring break with every test still
        // green.
        var repositorySettingsRepo = new RepositorySettingsRepository(db);
        var protector = new FakeSecretProtector();
        var repositorySettings = new StoredRepositorySettings(
            new SingleServiceScopeFactory(repositorySettingsRepo),
            protector,
            gitLabOptions,
            NullLogger<StoredRepositorySettings>.Instance);
        var connectionProbe = new RecordingConnectionProbe();

        // The real activity log, not a stub: recording is a side effect of
        // ordinary operations, and a stub here would let it silently stop
        // working while every test still passed.
        var activityRepo = new ActivityRepository(db);
        var activity = new ActivityLog(activityRepo, user, NullLogger<ActivityLog>.Instance);

        var llm = new ScriptedLlmProvider();

        // Composed exactly as AddServices + AddIntegrations do it, including
        // the null repository source: the fan-out and merge are only worth
        // testing against more than one source.
        // Through the catalog, as the app does: the document source plus
        // whatever repository servers the table holds. With none added it
        // supplies the stand-in, so the fan-out and merge still see two sources.
        var catalog = new KnowledgeSourceCatalog(
            [new DocumentKnowledgeSource(searchService), .. extraSources],
            settingRepo,
            new McpRepositoryKnowledgeSourceFactory(
                Options.Create(new KnowledgeSourceOptions()), NullLoggerFactory.Instance),
            Options.Create(new KnowledgeSourceOptions
            {
                RepositoryProvider = KnowledgeSourceOptions.McpProvider,
            }));

        var knowledge = new CompositeKnowledgeSource(
            catalog,
            Options.Create(knowledgeOptions),
            NullLogger<CompositeKnowledgeSource>.Instance);

        var ingestion = new IngestionService(
            documentRepo, chunkRepo, repository, extractors,
            new TextChunker(ingestionOptions), embeddings, queue, activity,
            NullLogger<IngestionService>.Instance);

        var syncQueue = new RecordingSyncQueue();

        var mirror = new RepositoryMirrorService(
            repository, documentRepo, folderRepo, syncStateRepo, ingestion, queue, activity,
            repositorySettings, NullLogger<RepositoryMirrorService>.Instance);

        var settingsAdmin = new RepositorySettingsAdmin(
            repositorySettingsRepo, repositorySettings, connectionProbe, protector, activity, user,
            NullLogger<RepositorySettingsAdmin>.Instance);

        return new Scope(
            db,
            repository,
            new FolderService(folderRepo),
            new DocumentService(documentRepo, chunkRepo, chatRepo, repository, activity),
            ingestion,
            mirror,
            new RepositoryWebhook(
                syncQueue, repositorySettings, NullLogger<RepositoryWebhook>.Instance),
            searchService,
            new ChatService(
                chatRepo, knowledge, llm, user,
                Options.Create(new ChatOptions()), NullLogger<ChatService>.Instance),
            chunkRepo,
            queue,
            syncQueue,
            llm,
            knowledge,
            user,
            settingRepo,
            activity,
            settingsAdmin,
            repositorySettings,
            connectionProbe,
            protector);
    }

    /// <summary>
    /// Returns whatever the test tells it to, and records the prompt it was
    /// given.
    ///
    /// A real model would make these tests non-deterministic and slow, and the
    /// behaviour under test is the orchestrator's — what it retrieves, what it
    /// does with a fabricated citation, whether it calls the model at all —
    /// none of which depends on a model being good.
    /// </summary>
    public sealed class ScriptedLlmProvider : ILlmProvider
    {
        public string Name => "scripted";

        /// <summary>What the next answer will be.</summary>
        public string Answer { get; set; } = "A grounded answer [1].";

        /// <summary>Set to throw mid-generation instead of answering.</summary>
        public Exception? Failure { get; set; }

        /// <summary>The system prompt of the most recent call, or null if never called.</summary>
        public string? LastPrompt { get; private set; }

        public int CallCount { get; private set; }

        public async IAsyncEnumerable<string> StreamAsync(
            string systemPrompt,
            IReadOnlyList<LlmMessage> messages,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            LastPrompt = systemPrompt;
            LastMessages = messages;
            CallCount++;

            if (Failure is not null) throw Failure;

            // Fragmented, so the streaming path is exercised rather than a
            // single-chunk shortcut that would hide reassembly bugs.
            foreach (var word in Answer.Split(' '))
            {
                await Task.Yield();
                yield return word + " ";
            }
        }

        public IReadOnlyList<LlmMessage> LastMessages { get; private set; } = [];

        public Task<LlmAvailability> CheckAvailabilityAsync(CancellationToken ct = default) =>
            Task.FromResult(new LlmAvailability(true, "Scripted."));
    }

    public sealed record Scope(
        DocHubDbContext Db,
        FakeSourceRepository Repository,
        IFolderService Folders,
        IDocumentService Documents,
        IIngestionService Ingestion,
        IRepositoryMirrorService Mirror,
        IRepositoryWebhook Webhook,
        ISearchService Search,
        IChatService Chat,
        IChunkRepository Chunks,
        RecordingIngestionQueue Queue,
        RecordingSyncQueue SyncQueue,
        ScriptedLlmProvider Llm,
        IKnowledgeRetriever Knowledge,
        TestCurrentUser User,
        IRepositorySourceSettingRepository SourceSettings,
        IActivityLog Activity,
        IRepositorySettingsAdmin RepositorySettings,
        IRepositorySettingsReader SettingsInForce,
        RecordingConnectionProbe ConnectionProbe,
        FakeSecretProtector Protector) : IAsyncDisposable
    {
        /// <summary>
        /// Commits a file to the fake repository and mirrors it, returning the
        /// document that resulted.
        ///
        /// The path is relative to the mirrored tree, so "guides/vpn.md" lands
        /// in the folder "docs/guides" — the project's own leaf name is the
        /// visible root, exactly as it is against a real GitLab.
        /// </summary>
        public async Task<DocumentViewModel> PublishAsync(string path, string content)
        {
            Repository.Put(path, content);
            await Mirror.SyncAsync(actorId: null);

            var documents = await Documents.QueryAsync(new DocumentQueryRequest { Take = 500 });

            return documents.Single(document => document.RepositoryPath == path.Trim('/'));
        }

        /// <summary>
        /// A folder holding nothing retrieval can see, for the tests that need
        /// a question to find no passages.
        ///
        /// A directory with no files in it cannot exist any more — folders are
        /// derived from file paths — so this mirrors one file and leaves it
        /// Pending. Retrieval only ever sees Indexed documents, so the effect is
        /// the same and the setup is one the product can actually reach.
        /// </summary>
        public async Task<Guid> EmptyFolderAsync(string directory) =>
            (await PublishAsync($"{directory}/not-indexed.md", "Nothing here yet.")).FolderId;

        /// <summary>Commits a file, mirrors it, and puts it all the way through ingestion.</summary>
        public async Task<DocumentViewModel> PublishIndexedAsync(string path, string content)
        {
            var document = await PublishAsync(path, content);
            await Ingestion.IngestAsync(document.Id);
            return document;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    /// <summary>
    /// The principal a test runs as. Mutable so a test can change role
    /// mid-scope and assert what that does, without a second fixture.
    /// </summary>
    public sealed class TestCurrentUser : ICurrentUser
    {
        public Guid Id { get; set; } = DocHubDbContext.SystemUserId;

        public string Role { get; set; } = Roles.Admin;

        public bool IsAuthenticated { get; set; } = true;
    }

    /// <summary>
    /// Records what would have been queued instead of running it. Ingestion is
    /// triggered explicitly in tests, so an assertion never races a worker.
    /// </summary>
    public sealed class RecordingIngestionQueue : IIngestionQueue
    {
        private readonly List<Guid> queued = [];

        public IReadOnlyList<Guid> Queued => queued;

        public void Enqueue(Guid documentId) => queued.Add(documentId);
    }

    /// <summary>
    /// Records what would have been queued instead of running it, so a test can
    /// assert that a webhook led to a sync without a job server.
    /// </summary>
    public sealed class RecordingSyncQueue : IRepositorySyncQueue
    {
        private readonly List<Guid?> queued = [];

        public IReadOnlyList<Guid?> Queued => queued;

        public void Enqueue(Guid? actorId) => queued.Add(actorId);
    }
}

[CollectionDefinition(nameof(StackCollection))]
public sealed class StackCollection : ICollectionFixture<StackFixture>;
