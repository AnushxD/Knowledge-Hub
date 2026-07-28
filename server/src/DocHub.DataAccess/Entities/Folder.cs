namespace DocHub.DataAccess.Entities;

/// <summary>
/// A user-defined folder. The hierarchy is self-referencing and arbitrarily
/// deep — the product principle is "no forced structure", so nothing here
/// constrains the shape a team chooses.
/// </summary>
public class Folder
{
    public Guid Id { get; set; }

    /// <summary>Null for a top-level folder.</summary>
    public Guid? ParentId { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// Materialised path ("Engineering/Onboarding"). Denormalised deliberately:
    /// breadcrumbs and descendant queries are read constantly and would
    /// otherwise need a recursive CTE on every request. Rewritten for the whole
    /// subtree whenever a folder is renamed or moved.
    /// </summary>
    public required string Path { get; set; }

    public Guid OwnerId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Folder? Parent { get; set; }

    public ICollection<Folder> Children { get; set; } = [];

    public ICollection<Document> Documents { get; set; } = [];

    public User? Owner { get; set; }
}
