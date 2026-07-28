using Azure.Storage.Blobs;
using DocHub.Integrations.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocHub.Integrations.Tests;

/// <summary>
/// Runs the blob storage implementation against the real Azurite container
/// from docker-compose. A fake would only prove the fake works — this exercises
/// the actual Azure SDK calls, including the 404 behaviour the code relies on.
///
/// Each run uses its own throwaway container so tests never collide with
/// development data or with a parallel run.
/// </summary>
public sealed class AzuriteFixture : IAsyncLifetime
{
    private const string DefaultConnection = "UseDevelopmentStorage=true";

    private readonly string _containerName = $"test-{Guid.NewGuid():N}";

    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("DOCHUB_TEST_BLOBS") ?? DefaultConnection;

    public IFileStorage Storage { get; private set; } = null!;

    private BlobServiceClient _client = null!;

    public Task InitializeAsync()
    {
        _client = new BlobServiceClient(ConnectionString);

        var options = Options.Create(new FileStorageOptions
        {
            ConnectionString = ConnectionString,
            ContainerName = _containerName,
        });

        // The implementation creates the container on first use, so there is
        // nothing to set up here — that path gets exercised by the first test.
        Storage = new AzureBlobFileStorage(
            _client,
            options,
            NullLogger<AzureBlobFileStorage>.Instance);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync() =>
        await _client.GetBlobContainerClient(_containerName).DeleteIfExistsAsync();

    public static Stream StreamOf(string content) =>
        new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
}

[CollectionDefinition(nameof(AzuriteCollection))]
public sealed class AzuriteCollection : ICollectionFixture<AzuriteFixture>;
