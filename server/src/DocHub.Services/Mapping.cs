using DocHub.DataAccess.Dtos;
using DocHub.DataAccess.Entities;
using DocHub.Services.Ingestion;
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

    /// <param name="webUrl">
    /// Where the file lives in GitLab. Passed in rather than derived here: it
    /// needs the repository client, and mapping stays a pure function of its
    /// arguments.
    /// </param>
    public static DocumentViewModel ToViewModel(this DocumentDto document, Uri webUrl) =>
        new(
            document.Id,
            document.FolderId,
            document.Title,
            document.Description,
            document.FileName,
            document.Extension,
            document.SizeBytes,
            document.RepositoryPath,
            webUrl.ToString(),
            document.CommitSha,
            document.Tags,
            // Lower-cased so the JSON contract is stable regardless of how the
            // enum is spelled in C#.
            document.Status.ToString().ToLowerInvariant(),
            document.FailureReason,
            document.ChunkCount,
            document.IsStarred,
            document.LastSyncedAt,
            document.CreatedAt,
            document.UpdatedAt);

    public static DocumentDetailViewModel ToViewModel(
        this DocumentDetailDto detail,
        Uri webUrl,
        IReadOnlyList<ChunkMatchDto> sections,
        int citedInAnswers) =>
        new(
            detail.Document.ToViewModel(webUrl),
            [.. detail.Breadcrumb.Select(ToViewModel)],
            [.. sections.Select(ToViewModel)],
            citedInAnswers);

    public static DocumentSectionViewModel ToViewModel(this ChunkMatchDto chunk) =>
        new(
            chunk.Ordinal,
            // A chunk always gets a label, so the preview never renders a blank
            // heading for a format that carries no structure.
            chunk.SectionRef ?? $"Section {chunk.Ordinal + 1}",
            chunk.Text,
            TextChunker.EstimateTokens(chunk.Text));

    public static LibraryStatsViewModel ToViewModel(this LibraryStatsDto stats) =>
        new(
            stats.Documents,
            stats.Indexed,
            stats.InPipeline,
            stats.Failed,
            stats.Folders,
            stats.ContentBytes,
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
