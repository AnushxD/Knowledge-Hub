namespace DocHub.Integrations.Storage;

/// <summary>
/// Blob storage configuration, bound from the "FileStorage" section.
///
/// Locally <see cref="ConnectionString"/> is "UseDevelopmentStorage=true",
/// which points the Azure SDK at the Azurite container; in production it is a
/// real Azure Blob Storage connection string. The implementation is identical
/// either way — only this value changes.
/// </summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Container that holds uploaded document files.</summary>
    public string ContainerName { get; set; } = "documents";
}
