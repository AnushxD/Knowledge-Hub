using DocHub.Integrations.Knowledge;
using DocHub.Integrations.SourceControl;
using DocHub.Services.Activity;
using DocHub.Services.Chat;
using DocHub.Services.Documents;
using DocHub.Services.Folders;
using DocHub.Services.Ingestion;
using DocHub.Services.Ingestion.Extraction;
using DocHub.Services.Knowledge;
using DocHub.Services.Repository;
using DocHub.Services.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DocHub.Services;

/// <summary>
/// Single registration entry point for the Service layer — business logic only.
/// </summary>
public static class ServicesServiceCollectionExtensions
{
    public static IServiceCollection AddServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<IngestionOptions>()
            .Bind(configuration.GetSection(IngestionOptions.SectionName))
            .Validate(
                options => options.TargetTokens > 0,
                "Ingestion:TargetTokens must be greater than zero.")
            .Validate(
                options => options.OverlapTokens < options.TargetTokens,
                "Ingestion:OverlapTokens must be smaller than TargetTokens, or chunking "
                + "would never advance.")
            .ValidateOnStart();

        services.AddScoped<IFolderService, FolderService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IIngestionService, IngestionService>();
        services.AddScoped<IRepositoryMirrorService, RepositoryMirrorService>();
        services.AddScoped<IRepositoryWebhook, RepositoryWebhook>();
        services.AddScoped<IRepositorySettingsAdmin, RepositorySettingsAdmin>();

        // Singleton, behind both of its contracts: it caches one snapshot for
        // the whole process, and saving refreshes that snapshot so the answer
        // the screen draws and the repository the next sync reads describe the
        // same place.
        //
        // This replaces the configuration-only reader registered by
        // AddIntegrations, which is why that one is a TryAdd and this one runs
        // after it.
        services.AddSingleton<StoredRepositorySettings>();
        services.AddSingleton<IRepositorySettingsReader>(
            provider => provider.GetRequiredService<StoredRepositorySettings>());
        services.AddSingleton<IRepositorySettingsRefresher>(
            provider => provider.GetRequiredService<StoredRepositorySettings>());

        // In-process encryption of the secrets that are stored in the database
        // rather than in configuration, so it belongs here beside the text
        // extractors rather than in Integrations.
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IActivityLog, ActivityLog>();

        // Scoped, not singleton: it wraps ISearchService and so inherits the
        // request-scoped DbContext underneath it. The external sources it fans
        // out to alongside this one are registered in AddIntegrations.
        services.AddScoped<IKnowledgeSource, DocumentKnowledgeSource>();
        services.AddScoped<IKnowledgeRetriever, CompositeKnowledgeSource>();

        services
            .AddOptions<KnowledgeOptions>()
            .Bind(configuration.GetSection(KnowledgeOptions.SectionName))
            .Validate(
                options => options.SourceTimeoutSeconds > 0,
                "Knowledge:SourceTimeoutSeconds must be greater than zero. Without a deadline "
                + "a hung source stalls every question.")
            .ValidateOnStart();

        // Resolves the live set of sources per request: the document source
        // from the container, plus one per repository server in the database.
        services.AddScoped<IKnowledgeSourceCatalog, KnowledgeSourceCatalog>();
        services.AddScoped<IRepositorySourceAdmin, RepositorySourceAdmin>();

        services
            .AddOptions<ChatOptions>()
            .Bind(configuration.GetSection(ChatOptions.SectionName))
            .Validate(
                options => options.PassageCount > 0,
                "Chat:PassageCount must be greater than zero.")
            .ValidateOnStart();

        // Singletons: extraction and chunking are stateless transformations
        // over their arguments, holding nothing per request.
        services.AddSingleton<ITextExtractor, PlainTextExtractor>();
        services.AddSingleton<ITextExtractor, PdfTextExtractor>();
        services.AddSingleton<ITextExtractor, OpenXmlTextExtractor>();
        services.AddSingleton<ITextExtractorRegistry, TextExtractorRegistry>();
        services.AddSingleton<ITextChunker, TextChunker>();

        // ICurrentUser is deliberately not registered here. Reading a principal
        // is the host's job — the API binds it to the authenticated request,
        // and a background job or a test binds it to whoever the work is being
        // done for.

        return services;
    }
}
