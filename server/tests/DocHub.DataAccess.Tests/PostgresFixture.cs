using Microsoft.EntityFrameworkCore;

namespace DocHub.DataAccess.Tests;

/// <summary>
/// Runs the repositories against a real Postgres rather than an in-memory
/// provider — materialised-path LIKE queries, text[] columns and the GIN index
/// are exactly the things an in-memory provider would fail to catch.
///
/// Uses a dedicated `dochub_test` database on the docker-compose instance, so
/// tests never touch development data. Start it with `docker compose up -d`.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string DefaultConnection =
        "Host=localhost;Port=5432;Database=dochub_test;Username=dochub;Password=dochub_local_dev";

    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("DOCHUB_TEST_DB") ?? DefaultConnection;

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        // Rebuilt per run so a failed run never leaves the next one on a half
        // migrated schema.
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
    }

    public DocHubDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DocHubDbContext>()
            // Mirrors AddDataAccess: without this Npgsql has no mapping for the
            // pgvector column and the model fails validation.
            .UseNpgsql(ConnectionString, npgsql => npgsql.UseVector())
            .Options;

        return new DocHubDbContext(options);
    }
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
