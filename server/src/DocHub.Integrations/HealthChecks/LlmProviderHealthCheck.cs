using DocHub.Integrations.Llm;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DocHub.Integrations.HealthChecks;

/// <summary>
/// Reports whether the answer-generating model can serve requests.
///
/// Degraded rather than Unhealthy, for the same reason as the embedding check:
/// documents still upload, index and search with no model — only the assistant
/// stops working — so this must not take the whole instance out of rotation.
/// </summary>
internal sealed class LlmProviderHealthCheck(ILlmProvider llm) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object> { ["provider"] = llm.Name };

        try
        {
            var availability = await llm.CheckAvailabilityAsync(cancellationToken);

            return availability.IsAvailable
                ? HealthCheckResult.Healthy(availability.Detail, data)
                : HealthCheckResult.Degraded(availability.Detail, data: data);
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Degraded(
                "Could not probe the model provider.", exception, data);
        }
    }
}
