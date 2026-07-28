namespace DocHub.DataAccess.Entities;

/// <summary>
/// A person who owns folders and documents.
///
/// Phase 1 needs an owner for metadata, so this table exists and is seeded with
/// a single local development user. Real authentication (ASP.NET Core Identity,
/// then Entra ID) and role enforcement arrive in phase 5 — at which point this
/// row is replaced by real principals, not the other way round.
/// </summary>
public class User
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Email { get; set; }

    /// <summary>Admin / Editor / Viewer. Not enforced until phase 5.</summary>
    public string Role { get; set; } = "Viewer";

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Folder> Folders { get; set; } = [];

    public ICollection<Document> Documents { get; set; } = [];
}
