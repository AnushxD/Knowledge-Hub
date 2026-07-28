using DocHub.Integrations.Embeddings;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DocHub.Integrations.HealthChecks;

/// <summary>
/// Reports whether the embedding provider can actually serve requests.
///
/// Degraded rather than Unhealthy: documents still upload, list and download
/// with no embeddings — only ingestion and the vector half of search stop
/// working — so this must not take the whole instance out of rotation.
/// </summary>
internal sealed class EmbeddingProviderHealthCheck(IEmbeddingProvider embeddings) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["provider"] = embeddings.Name,
            ["dimensions"] = embeddings.Dimensions,
        };

        try
        {
            var availability = await embeddings.CheckAvailabilityAsync(cancellationToken);

            return availability.IsAvailable
                ? HealthCheckResult.Healthy(availability.Detail, data)
                : HealthCheckResult.Degraded(availability.Detail, data: data);
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Degraded(
                "Could not probe the embedding provider.", exception, data);
        }
    }
}
