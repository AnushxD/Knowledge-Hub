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

        services.AddHealthChecks()
            .AddCheck<BlobStorageHealthCheck>("blob-storage", tags: ["ready", "storage"]);

        return services;
    }
}
