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

            var data = new Dictionary<string, object>
            {
                ["account"] = client.AccountName,
                ["container"] = _options.ContainerName,
                ["containerExists"] = exists.Value,
            };

            // The container is provisioned by an explicit setup step, never by
            // the app at runtime. Missing means setup has not been run, so say
            // so — and name the command — rather than failing later on upload.
            if (!exists.Value)
            {
                return HealthCheckResult.Degraded(
                    $"Blob storage is reachable but the '{_options.ContainerName}' container "
                        + "does not exist. Run: dotnet run --project server/src/DocHub.Api -- init-storage",
                    data: data);
            }

            return HealthCheckResult.Healthy("Blob storage reachable.", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Cannot reach blob storage.", ex);
        }
    }
}
