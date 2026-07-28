using Azure.Storage.Blobs;
using DocHub.Integrations.HealthChecks;
using DocHub.Integrations.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DocHub.Integrations;

/// <summary>
/// Single registration entry point for the Integrations layer — external
/// systems only (blob storage now; LLM, embeddings and MCP in later phases).
/// </summary>
public static class IntegrationsServiceCollectionExtensions
{
    public static IServiceCollection AddIntegrations(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                "FileStorage:ConnectionString must be configured.")
            .ValidateOnStart();

        // Registered as a singleton: BlobServiceClient is thread-safe and holds
        // the connection pool, so creating one per request wastes sockets.
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<FileStorageOptions>>().Value;
            return new BlobServiceClient(options.ConnectionString);
        });

        // Singleton to match BlobServiceClient's lifetime; the implementation
        // holds no per-request state.
        services.AddSingleton<IFileStorage, AzureBlobFileStorage>();

        services.AddHealthChecks()
            .AddCheck<BlobStorageHealthCheck>("blob-storage", tags: ["ready", "storage"]);

        return services;
    }

    /// <summary>
    /// Prepares external systems at startup — currently just the blob
    /// container. Mirrors <c>MigrateDataAccessAsync</c>, so the host has one
    /// obvious place to make each layer ready before serving traffic.
    /// </summary>
    public static async Task InitializeIntegrationsAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        await storage.EnsureReadyAsync();
    }
}
