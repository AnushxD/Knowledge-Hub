using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DocHub.DataAccess.HealthChecks;

/// <summary>
/// Confirms the API can actually reach Postgres, and that the pgvector
/// extension the phase 2 ingestion pipeline depends on is installed.
/// Probing infrastructure belongs in the layer that owns it, so the API never
/// opens a connection itself.
/// </summary>
internal sealed class PostgresHealthCheck(IOptions<DataAccessOptions> options) : IHealthCheck
{
    private readonly DataAccessOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlDataSourceBuilder(_options.ConnectionString)
                .Build()
                .CreateConnection();

            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                "select default_version from pg_available_extensions where name = 'vector'";
            var vectorVersion = await command.ExecuteScalarAsync(cancellationToken) as string;

            var data = new Dictionary<string, object>
            {
                ["server"] = connection.PostgreSqlVersion.ToString(),
                ["pgvector"] = vectorVersion ?? "not available",
            };

            return vectorVersion is null
                ? HealthCheckResult.Degraded(
                    "Postgres is reachable but the pgvector extension is not available.", data: data)
                : HealthCheckResult.Healthy("Postgres reachable.", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Cannot reach Postgres.", ex);
        }
    }
}
