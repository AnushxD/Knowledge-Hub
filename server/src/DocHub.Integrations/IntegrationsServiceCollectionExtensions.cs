using Azure.Storage.Blobs;
using DocHub.Integrations.Embeddings;
using DocHub.Integrations.HealthChecks;
using DocHub.Integrations.Knowledge;
using DocHub.Integrations.Llm;
using DocHub.Integrations.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DocHub.Integrations;

/// <summary>
/// Single registration entry point for the Integrations layer — external
/// systems only (blob storage, LLM, embeddings, and the knowledge sources that
/// come from outside the hub).
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

        services
            .AddOptions<EmbeddingOptions>()
            .Bind(configuration.GetSection(EmbeddingOptions.SectionName))
            .Validate(
                options => options.Dimensions > 0,
                "Embeddings:Dimensions must be greater than zero.")
            .Validate(
                options => options.Provider is EmbeddingOptions.OllamaProvider
                    or EmbeddingOptions.HashingProvider,
                $"Embeddings:Provider must be '{EmbeddingOptions.OllamaProvider}' or "
                + $"'{EmbeddingOptions.HashingProvider}'.")
            .ValidateOnStart();

        var embeddingOptions = configuration
            .GetSection(EmbeddingOptions.SectionName)
            .Get<EmbeddingOptions>() ?? new EmbeddingOptions();

        // The provider is chosen from configuration at startup, not probed at
        // run time: search quality depends on every vector in the table coming
        // from the same model, so silently falling back mid-run would poison
        // the index with incomparable embeddings.
        if (embeddingOptions.Provider == EmbeddingOptions.HashingProvider)
        {
            services.AddSingleton<IEmbeddingProvider, HashingEmbeddingProvider>();
        }
        else
        {
            services
                .AddHttpClient<IEmbeddingProvider, OllamaEmbeddingProvider>(client =>
                {
                    client.BaseAddress = new Uri(embeddingOptions.BaseUrl);
                    client.Timeout = TimeSpan.FromSeconds(embeddingOptions.TimeoutSeconds);
                });
        }

        services
            .AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName))
            .Validate(
                options => options.MaxOutputTokens > 0,
                "Llm:MaxOutputTokens must be greater than zero.")
            .Validate(
                options => options.ContextTokens >= 2048,
                "Llm:ContextTokens must be at least 2048. Below that the grounded prompt is "
                + "silently truncated and the assistant answers without seeing its sources.")
            .Validate(
                options => options.Provider == LlmOptions.OllamaProvider,
                $"Llm:Provider must be '{LlmOptions.OllamaProvider}'. Adding a hosted provider "
                + "means one more ILlmProvider implementation and one more branch here.")
            .ValidateOnStart();

        var llmOptions = configuration
            .GetSection(LlmOptions.SectionName)
            .Get<LlmOptions>() ?? new LlmOptions();

        services
            .AddHttpClient<ILlmProvider, OllamaLlmProvider>(client =>
            {
                client.BaseAddress = new Uri(llmOptions.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(llmOptions.TimeoutSeconds);
            });

        services
            .AddOptions<KnowledgeSourceOptions>()
            .Bind(configuration.GetSection(KnowledgeSourceOptions.SectionName))
            .Validate(
                options => options.RepositoryProvider == KnowledgeSourceOptions.NoneProvider,
                $"KnowledgeSources:RepositoryProvider must be "
                + $"'{KnowledgeSourceOptions.NoneProvider}'. The "
                + $"'{KnowledgeSourceOptions.McpProvider}' provider arrives with the real MCP "
                + "client in phase 7 — failing at startup is better than a source that silently "
                + "contributes nothing while claiming to be connected.")
            .ValidateOnStart();

        // Registered as a source among others rather than special-cased: the
        // composite must see more than one source locally, or the fan-out is
        // only ever exercised in production.
        //
        // Scoped, not singleton: it reads the administrator's current setting
        // per request, so a change in the UI takes effect on the next question
        // rather than on the next application pool recycle.
        services.AddScoped<IKnowledgeSource, NullRepositoryKnowledgeSource>();

        services.AddHealthChecks()
            .AddCheck<BlobStorageHealthCheck>("blob-storage", tags: ["ready", "storage"])
            .AddCheck<EmbeddingProviderHealthCheck>("embeddings", tags: ["ready", "ai"])
            .AddCheck<LlmProviderHealthCheck>("assistant-model", tags: ["ready", "ai"]);

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
