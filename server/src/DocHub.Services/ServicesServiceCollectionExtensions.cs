using DocHub.Services.Documents;
using DocHub.Services.Folders;
using Microsoft.Extensions.DependencyInjection;

namespace DocHub.Services;

/// <summary>
/// Single registration entry point for the Service layer — business logic only.
/// </summary>
public static class ServicesServiceCollectionExtensions
{
    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddScoped<IFolderService, FolderService>();
        services.AddScoped<IDocumentService, DocumentService>();

        // Replaced in phase 5 by an implementation reading the authenticated
        // principal from the request.
        services.AddScoped<ICurrentUser, SeededCurrentUser>();

        return services;
    }
}
