using Azure.Storage.Blobs;
using DocHub.DataAccess;
using DocHub.DataAccess.Repositories;
using DocHub.Integrations.Embeddings;
using DocHub.Integrations.Storage;
using DocHub.Services;
using DocHub.Services.Documents;
using DocHub.Services.Folders;
using DocHub.Services.Ingestion;
using DocHub.Services.Ingestion.Extraction;
using DocHub.Services.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocHub.Services.Tests;

/// <summary>
/// Wires the real Service layer to the real repositories and the real blob
/// storage — the whole stack minus HTTP.
///
/// Mocking the repository or storage here would only prove the mocks behave;
/// the interesting bugs live in the seams between layers, such as whether a
/// deleted folder actually frees the blobs its documents owned.
/// </summary>
public sealed class StackFixture : IAsyncLifetime
{
    private const string DefaultDb =
        "Host=localhost;Port=5432;Database=dochub_services_test;Username=dochub;Password=dochub_local_dev";

    private readonly string _containerName = $"svc-{Guid.NewGuid():N}";

    private string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("DOCHUB_TEST_DB") ?? DefaultDb;

    private string BlobConnection { get; } =
        Environment.GetEnvironmentVariable("DOCHUB_TEST_BLOBS") ?? "UseDevelopmentStorage=true";

    private BlobServiceClient _blobClient = null!;

    public IFileStorage Storage { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        _blobClient = new BlobServiceClient(BlobConnection);
        Storage = new AzureBlobFileStorage(
            _blobClient,
            Options.Create(new FileStorageOptions
            {
                ConnectionString = BlobConnection,
                ContainerName = _containerName,
            }),
            NullLogger<AzureBlobFileStorage>.Instance);

        await Storage.EnsureReadyAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
        await _blobClient.GetBlobContainerClient(_containerName).DeleteIfExistsAsync();
    }

    private DocHubDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DocHubDbContext>()
            // Mirrors AddDataAccess: without this Npgsql has no mapping for the
            // pgvector column and the model fails validation.
            .UseNpgsql(ConnectionString, npgsql => npgsql.UseVector())
            .Options);

    /// <summary>The services of one request, sharing a DbContext as a request would.</summary>
    public Scope NewScope()
    {
        var db = CreateContext();
        var folderRepo = new FolderRepository(db);
        var documentRepo = new DocumentRepository(db);
        var chunkRepo = new ChunkRepository(db);
        var user = new TestCurrentUser();
        var queue = new RecordingIngestionQueue();

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

        return new Scope(
            db,
            new FolderService(folderRepo, Storage, user, NullLogger<FolderService>.Instance),
            new DocumentService(
                documentRepo, folderRepo, chunkRepo, Storage, queue, user,
                NullLogger<DocumentService>.Instance),
            new IngestionService(
                documentRepo, chunkRepo, Storage, extractors,
                new TextChunker(ingestionOptions), embeddings, queue,
                NullLogger<IngestionService>.Instance),
            new SearchService(
                chunkRepo, embeddings, NullLogger<SearchService>.Instance),
            chunkRepo,
            queue);
    }

    public sealed record Scope(
        DocHubDbContext Db,
        IFolderService Folders,
        IDocumentService Documents,
        IIngestionService Ingestion,
        ISearchService Search,
        IChunkRepository Chunks,
        RecordingIngestionQueue Queue) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid Id => DocHubDbContext.SystemUserId;
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

    public static Stream FileOf(string content) =>
        new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
}

[CollectionDefinition(nameof(StackCollection))]
public sealed class StackCollection : ICollectionFixture<StackFixture>;
