namespace DocHub.DataAccess;

/// <summary>
/// Strongly-typed configuration for the relational store, bound from the
/// "Database" section. One Options class per external dependency.
/// </summary>
public sealed class DataAccessOptions
{
    public const string SectionName = "Database";

    /// <summary>Npgsql connection string for the DocHub database.</summary>
    public string ConnectionString { get; set; } = string.Empty;
}
