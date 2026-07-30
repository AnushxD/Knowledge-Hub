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

    /// <summary>
    /// The storage REST API version to speak, as a date — for example
    /// <c>2024-08-04</c>. Empty means the SDK's own default, which is the
    /// newest version it knows.
    ///
    /// This exists for Azurite. The emulator implements one specific service
    /// version and rejects anything newer outright, with
    /// <c>InvalidHeaderValue: The API version … is not supported by Azurite</c>
    /// — so a current SDK talking to an emulator that has not caught up fails
    /// every upload. Pinning the version here makes the modern client speak the
    /// older protocol.
    ///
    /// Preferred over downgrading the <c>Azure.Storage.Blobs</c> package,
    /// because it keeps the SDK's fixes, is per-environment rather than
    /// repo-wide, and leaves real Azure on the newest version simply by not
    /// setting it. Preferred over `--skipApiVersionCheck` only when the
    /// emulator's launch arguments are not yours to change.
    /// </summary>
    public string ServiceVersion { get; set; } = string.Empty;
}
