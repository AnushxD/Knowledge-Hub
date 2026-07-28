using Azure.Storage.Blobs;
using DocHub.DataAccess;
using DocHub.DataAccess.Repositories;
using DocHub.Integrations.Storage;
using DocHub.Services;
using DocHub.Services.Documents;
using DocHub.Services.Folders;
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
        new(new DbContextOptionsBuilder<DocHubDbContext>().UseNpgsql(ConnectionString).Options);

    /// <summary>A service pair sharing one DbContext, as a request would.</summary>
    public Scope NewScope()
    {
        var db = CreateContext();
        var folderRepo = new FolderRepository(db);
        var documentRepo = new DocumentRepository(db);
        var user = new TestCurrentUser();

        return new Scope(
            db,
            new FolderService(folderRepo, Storage, user, NullLogger<FolderService>.Instance),
            new DocumentService(
                documentRepo, folderRepo, Storage, user, NullLogger<DocumentService>.Instance));
    }

    public sealed record Scope(
        DocHubDbContext Db,
        IFolderService Folders,
        IDocumentService Documents) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid Id => DocHubDbContext.SystemUserId;
    }

    public static Stream FileOf(string content) =>
        new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
}

[CollectionDefinition(nameof(StackCollection))]
public sealed class StackCollection : ICollectionFixture<StackFixture>;
