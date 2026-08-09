namespace DocHub.DataAccess.Entities;

/// <summary>
/// A directory in the mirrored repository. The hierarchy is self-referencing
/// and arbitrarily deep, and its shape is not ours to choose — it is whatever
/// the team maintains in GitLab, reproduced here so the tree on screen matches
/// the tree in the repository.
///
/// No owner: nobody creates a folder in the hub any more, so recording who
/// would be recording the sync.
/// </summary>
public class Folder
{
    public Guid Id { get; set; }

    /// <summary>Null for the repository root.</summary>
    public Guid? ParentId { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// Materialised path ("Engineering/Onboarding"), matching the repository
    /// path with the configured sub-path stripped. Denormalised deliberately:
    /// breadcrumbs and descendant queries are read constantly and would
    /// otherwise need a recursive CTE on every request.
    /// </summary>
    public required string Path { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Folder? Parent { get; set; }

    public ICollection<Folder> Children { get; set; } = [];

    public ICollection<Document> Documents { get; set; } = [];
}
