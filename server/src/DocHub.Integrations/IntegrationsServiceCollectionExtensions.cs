using Azure.Storage.Blobs;
using DocHub.Integrations.Embeddings;
using DocHub.Integrations.HealthChecks;
using DocHub.Integrations.Knowledge;
using DocHub.Integrations.Llm;
using DocHub.Integrations.SourceControl;
using DocHub.Integrations.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
            // Caught at startup rather than on the first upload: a typo here
            // would otherwise look like a storage outage half a screen into the
            // app, long after the person who set it has moved on.
            .Validate(
                options => TryParseServiceVersion(options.ServiceVersion, out _),
                "FileStorage:ServiceVersion must be a storage REST API version such as "
                + "'2024-08-04', or empty to use the SDK default.")
            .ValidateOnStart();

        // Registered as a singleton: BlobServiceClient is thread-safe and holds
        // the connection pool, so creating one per request wastes sockets.
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<FileStorageOptions>>().Value;

            TryParseServiceVersion(options.ServiceVersion, out var version);

            return version is null
                ? new BlobServiceClient(options.ConnectionString)
                : new BlobServiceClient(options.ConnectionString, new BlobClientOptions(version.Value));
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

        var knowledgeOptions =
            configuration.GetSection(KnowledgeSourceOptions.SectionName).Get<KnowledgeSourceOptions>()
            ?? new KnowledgeSourceOptions();

        services
            .AddOptions<KnowledgeSourceOptions>()
            .Bind(configuration.GetSection(KnowledgeSourceOptions.SectionName))
            .Validate(
                options => options.RepositoryProvider is KnowledgeSourceOptions.NoneProvider
                    or KnowledgeSourceOptions.McpProvider,
                $"KnowledgeSources:RepositoryProvider must be "
                + $"'{KnowledgeSourceOptions.NoneProvider}' or "
                + $"'{KnowledgeSourceOptions.McpProvider}'.")
            .Validate(
                options => options.RepositoryMaxResults > 0,
                "KnowledgeSources:RepositoryMaxResults must be greater than zero.")
            .ValidateOnStart();

        // No IKnowledgeSource registrations for repositories: which servers
        // exist is a table an administrator edits, so the set cannot be known
        // when the container is built. The catalog in Services reads that table
        // per request and calls this factory — which is what lets a server
        // added in the UI be searched by the very next question.
        //
        // Singleton: it holds only options and a logger factory, and the
        // per-request state is the descriptor it is handed.
        services.AddSingleton<IRepositoryKnowledgeSourceFactory,
            McpRepositoryKnowledgeSourceFactory>();

        // The HttpClient here is only the fallback probe, reached when the MCP
        // handshake fails. Short timeout: by then the question is just "is
        // anything listening", a person is waiting, and a quick "could not
        // connect" beats a long wait for the same answer. The MCP path carries
        // its own, longer deadline.
        services
            .AddHttpClient<IRepositoryEndpointProbe, McpRepositoryEndpointProbe>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
            });

        services
            .AddOptions<GitLabOptions>()
            .Bind(configuration.GetSection(GitLabOptions.SectionName))
            // Checked for being *well formed*, not for being present. The
            // repository is now something an administrator can point at from
            // the UI, so an empty section is a first-run state rather than a
            // misconfiguration — and refusing to boot without one would mean
            // the screen that sets it could never be reached. A value that is
            // there and wrong is still caught here, where it is cheap.
            .Validate(
                options => string.IsNullOrWhiteSpace(options.BaseUrl)
                    || (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var url)
                        && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)),
                "GitLab:BaseUrl must be an absolute http or https URL, such as "
                + "'https://gitlab.example.org', or empty to be set in the UI.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Branch),
                "GitLab:Branch must name the branch to mirror.")
            .Validate(
                options => options.MaxFileBytes > 0,
                "GitLab:MaxFileBytes must be greater than zero.")
            .ValidateOnStart();

        var gitLabOptions = configuration
            .GetSection(GitLabOptions.SectionName)
            .Get<GitLabOptions>() ?? new GitLabOptions();

        // Configuration only. Services replaces this with the reader that
        // overlays what an administrator saved — which needs the database, and
        // so cannot live here. TryAdd rather than Add: the later registration
        // is the one that must win, and two would leave that to ordering.
        services.TryAddSingleton<IRepositorySettingsReader, ConfiguredRepositorySettings>();

        services
            .AddHttpClient<ISourceRepositoryClient, GitLabRepositoryClient>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(gitLabOptions.TimeoutSeconds);
            });

        // Its own client, with its own deadline: somebody is watching this one,
        // and a probe that takes as long as a full tree listing to say "that
        // address is wrong" is a probe nobody presses twice.
        services
            .AddHttpClient<IRepositoryConnectionProbe, GitLabConnectionProbe>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            });

        services.AddHealthChecks()
            .AddCheck<BlobStorageHealthCheck>("blob-storage", tags: ["ready", "storage"])
            .AddCheck<SourceRepositoryHealthCheck>("repository", tags: ["ready", "repository"])
            .AddCheck<EmbeddingProviderHealthCheck>("embeddings", tags: ["ready", "ai"])
            .AddCheck<LlmProviderHealthCheck>("assistant-model", tags: ["ready", "ai"]);

        return services;
    }

    /// <summary>
    /// Turns a configured storage API version into the SDK's enum.
    /// </summary>
    /// <param name="configured">
    /// A version as a date, <c>2024-08-04</c>, which is how Azure documents them
    /// and how Azurite names them in its error. The SDK spells the same thing
    /// <c>V2024_08_04</c>, so the two forms are bridged here rather than making
    /// whoever edits configuration know the C# identifier. Empty is valid and
    /// means "leave the SDK on its default".
    /// </param>
    /// <param name="version">The parsed version, or null when none was configured.</param>
    /// <returns>False only when a value was given and could not be understood.</returns>
    private static bool TryParseServiceVersion(
        string? configured,
        out BlobClientOptions.ServiceVersion? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(configured)) return true;

        var name = configured.Trim();

        // Accept the C# spelling too, so a value copied from IntelliSense works.
        if (!name.StartsWith('V')) name = "V" + name;
        name = name.Replace('-', '_');

        if (!Enum.TryParse<BlobClientOptions.ServiceVersion>(name, out var parsed)) return false;

        version = parsed;
        return true;
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
