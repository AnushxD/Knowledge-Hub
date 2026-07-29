using DocHub.Integrations.Knowledge;
using DocHub.Services.Chat;
using DocHub.Services.Documents;
using DocHub.Services.Folders;
using DocHub.Services.Ingestion;
using DocHub.Services.Ingestion.Extraction;
using DocHub.Services.Knowledge;
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
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IChatService, ChatService>();

        // Scoped, not singleton: it wraps ISearchService and so inherits the
        // request-scoped DbContext underneath it. The external sources it fans
        // out to alongside this one are registered in AddIntegrations.
        services.AddScoped<IKnowledgeSource, DocumentKnowledgeSource>();
        services.AddScoped<IKnowledgeRetriever, CompositeKnowledgeSource>();

        // Contract in Integrations, implementation here — only this layer can see
        // both the stored override and the configured baseline.
        services.AddScoped<IRepositorySourceSettings, RepositorySourceSettings>();

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
