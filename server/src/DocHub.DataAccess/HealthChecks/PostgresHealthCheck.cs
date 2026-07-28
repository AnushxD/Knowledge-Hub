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
                """
                select
                    (select default_version from pg_available_extensions where name = 'vector'),
                    to_regclass('public.__ef_migrations_history') is not null
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var vectorVersion = reader.IsDBNull(0) ? null : reader.GetString(0);
            var migrationsApplied = reader.GetBoolean(1);

            var data = new Dictionary<string, object>
            {
                ["server"] = connection.PostgreSqlVersion.ToString(),
                ["pgvector"] = vectorVersion ?? "not available",
                ["migrationsApplied"] = migrationsApplied,
            };

            // Setup is a deliberate manual step, so an un-migrated database is
            // a normal state to report clearly rather than a crash — the
            // message names the exact command to fix it.
            if (!migrationsApplied)
            {
                return HealthCheckResult.Degraded(
                    "Postgres is reachable but no migrations have been applied. "
                        + "Run: dotnet ef database update --project server/src/DocHub.DataAccess "
                        + "--startup-project server/src/DocHub.Api",
                    data: data);
            }

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
