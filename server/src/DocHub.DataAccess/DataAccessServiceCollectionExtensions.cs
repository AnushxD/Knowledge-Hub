using DocHub.DataAccess.HealthChecks;
using DocHub.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DocHub.DataAccess;

/// <summary>
/// Single registration entry point for the Data Access layer, so the API host
/// composes layers rather than knowing what each one is made of.
/// </summary>
public static class DataAccessServiceCollectionExtensions
{
    public static IServiceCollection AddDataAccess(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<DataAccessOptions>()
            .Bind(configuration.GetSection(DataAccessOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                "Database:ConnectionString must be configured.")
            .ValidateOnStart();

        services.AddDbContext<DocHubDbContext>((provider, builder) =>
        {
            var options = provider.GetRequiredService<IOptions<DataAccessOptions>>().Value;
            builder.UseNpgsql(options.ConnectionString, npgsql => npgsql
                .MigrationsHistoryTable("__ef_migrations_history")
                // Teaches Npgsql the pgvector type used by document_chunks.
                .UseVector());
        });

        // Scoped: repositories wrap the request-scoped DbContext.
        services.AddScoped<IFolderRepository, FolderRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IChunkRepository, ChunkRepository>();
        services.AddScoped<IChatRepository, ChatRepository>();
        services.AddScoped<IActivityRepository, ActivityRepository>();
        services.AddScoped<IRepositorySourceSettingRepository,
            RepositorySourceSettingRepository>();
        services.AddScoped<IRepositorySettingsRepository, RepositorySettingsRepository>();
        services.AddScoped<IRepositorySyncStateRepository, RepositorySyncStateRepository>();

        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready", "db"]);

        return services;
    }
}
