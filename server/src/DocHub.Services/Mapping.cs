using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;
using DocHub.Services.ViewModels;

namespace DocHub.Services;

/// <summary>
/// DTO to ViewModel conversion, in one place so the public shape of the API is
/// defined by exactly one file.
/// </summary>
internal static class Mapping
{
    public static FolderViewModel ToViewModel(this FolderDto folder) =>
        new(folder.Id, folder.ParentId, folder.Name, folder.Path, folder.DocumentCount);

    public static UserViewModel ToViewModel(this UserDto user) =>
        new(user.Id, user.Name, user.Email, Initials(user.Name));

    public static DocumentViewModel ToViewModel(this DocumentDto document) =>
        new(
            document.Id,
            document.FolderId,
            document.Title,
            document.Description,
            document.FileName,
            document.Extension,
            document.SizeBytes,
            document.Version,
            document.Tags,
            document.Owner.ToViewModel(),
            // Lower-cased so the JSON contract is stable regardless of how the
            // enum is spelled in C#.
            document.Status.ToString().ToLowerInvariant(),
            document.FailureReason,
            document.ChunkCount,
            document.IsStarred,
            document.CreatedAt,
            document.UpdatedAt);

    public static DocumentVersionViewModel ToViewModel(this DocumentVersionDto version) =>
        new(
            version.VersionNumber,
            version.SizeBytes,
            version.Note,
            version.ChangedBy.ToViewModel(),
            version.ChangedAt);

    public static DocumentDetailViewModel ToViewModel(this DocumentDetailDto detail) =>
        new(
            detail.Document.ToViewModel(),
            [.. detail.Breadcrumb.Select(ToViewModel)],
            [.. detail.Versions.Select(ToViewModel)],
            // Populated by the phase 2 ingestion pipeline.
            []);

    public static LibraryStatsViewModel ToViewModel(this LibraryStatsDto stats) =>
        new(
            stats.Documents,
            stats.Indexed,
            stats.InPipeline,
            stats.Failed,
            stats.Folders,
            stats.StorageBytes,
            stats.Chunks);

    public static IngestionStatus? ParseStatus(string value) =>
        Enum.TryParse<IngestionStatus>(value, ignoreCase: true, out var status) ? status : null;

    public static DocumentSort ParseSort(string? value) => value switch
    {
        "updated-asc" => DocumentSort.UpdatedAscending,
        "name-asc" => DocumentSort.NameAscending,
        "name-desc" => DocumentSort.NameDescending,
        "size-desc" => DocumentSort.SizeDescending,
        _ => DocumentSort.UpdatedDescending,
    };

    /// <summary>"Ana Ruiz" becomes "AR"; the client shows these in avatars.</summary>
    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant(),
        };
    }
}
