using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DocHub.Integrations.HealthChecks;

using DocHub.Integrations.Storage;

/// <summary>
/// Confirms blob storage is reachable — Azurite locally, real Azure Blob
/// Storage in production. Uploads fail loudly and early if this is red.
/// </summary>
internal sealed class BlobStorageHealthCheck(
    BlobServiceClient client,
    IOptions<FileStorageOptions> options) : IHealthCheck
{
    private readonly FileStorageOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var container = client.GetBlobContainerClient(_options.ContainerName);
            var exists = await container.ExistsAsync(cancellationToken);

            return HealthCheckResult.Healthy("Blob storage reachable.", new Dictionary<string, object>
            {
                ["account"] = client.AccountName,
                ["container"] = _options.ContainerName,
                ["containerExists"] = exists.Value,
            });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Cannot reach blob storage.", ex);
        }
    }
}
