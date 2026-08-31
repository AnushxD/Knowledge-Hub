using DocHub.Integrations.SourceControl;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DocHub.Integrations.HealthChecks;

/// <summary>
/// Confirms the mirrored repository can be read: the instance answers, the
/// token is accepted, and the configured project and branch exist.
///
/// Asks for the head commit rather than the tree. It exercises the same
/// address, credential and permission, and costs one small response instead of
/// paging a whole repository every time something polls readiness.
/// </summary>
internal sealed class SourceRepositoryHealthCheck(
    ISourceRepositoryClient repository,
    IRepositorySettingsReader settings) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var current = await settings.GetAsync(cancellationToken);

        var data = new Dictionary<string, object>
        {
            ["project"] = current.ProjectPath,
            ["branch"] = current.Branch,
            ["subPath"] = current.SubPath,
            ["authenticated"] = !string.IsNullOrWhiteSpace(current.Token),
            ["settingsFrom"] = current.Origin.ToString(),
        };

        // Nothing to check yet, and nothing broken either. A hub that has not
        // been pointed at a repository reports the state it is in, because
        // unhealthy here would have an operator hunting for an outage that is
        // really a screen nobody has filled in.
        if (!current.IsConfigured)
        {
            return HealthCheckResult.Degraded(
                "No repository is configured. An administrator can point the hub at one under "
                + "Settings.",
                data: data);
        }

        try
        {
            var head = await repository.GetHeadCommitAsync(cancellationToken);

            // Reachable, permitted, and pointed at a branch with no commits on
            // it. Nothing is broken, but nothing will ever be mirrored either,
            // which is worth saying out loud rather than reporting as healthy.
            if (head is null)
            {
                return HealthCheckResult.Degraded(
                    $"Branch '{repository.Branch}' of '{repository.ProjectPath}' has no commits, "
                    + "so there is nothing to mirror.",
                    data: data);
            }

            data["headCommit"] = head;
            return HealthCheckResult.Healthy("Repository reachable.", data);
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "Cannot read the mirrored repository.", exception, data);
        }
    }
}
