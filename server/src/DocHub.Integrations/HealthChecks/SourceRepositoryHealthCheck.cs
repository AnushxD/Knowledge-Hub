using DocHub.Integrations.SourceControl;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

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
    IOptions<GitLabOptions> options) : IHealthCheck
{
    private readonly GitLabOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["project"] = repository.ProjectPath,
            ["branch"] = repository.Branch,
            ["subPath"] = _options.SubPath,
            ["authenticated"] = !string.IsNullOrWhiteSpace(_options.Token),
        };

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
