using Microsoft.AspNetCore.Identity;

namespace DocHub.DataAccess.Entities;

/// <summary>
/// A person who signs in, and who owns folders and documents.
///
/// This is the Identity user store rather than a table beside one. Keeping the
/// credential and the domain owner as a single row means every existing
/// <c>owner_id</c> foreign key still points at the same key after
/// authentication arrives, and there is no pair of records that can drift apart
/// — the failure mode being a document owned by a user who can no longer sign
/// in, or two "users" for one person.
///
/// <see cref="IdentityUser{TKey}"/> supplies the id, email, password hash and
/// the stamps Identity needs; everything declared here is ours.
/// </summary>
public class User : IdentityUser<Guid>
{
    public required string Name { get; set; }

    /// <summary>
    /// Admin / Editor / Viewer, surfaced as a role claim at sign-in.
    ///
    /// Held as a column rather than through Identity's role tables because a
    /// person has exactly one role here, and one column is both the simplest
    /// honest model of that and the easiest thing for Entra ID to drive later:
    /// a directory group maps onto this one value.
    /// </summary>
    public string Role { get; set; } = Roles.Viewer;

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Folder> Folders { get; set; } = [];

    public ICollection<Document> Documents { get; set; } = [];
}

/// <summary>
/// The three roles, as constants rather than loose strings — a typo in an
/// <c>[Authorize]</c> attribute is otherwise a silent grant of nothing, or of
/// everything.
/// </summary>
public static class Roles
{
    /// <summary>Full access, including user administration and the dashboards.</summary>
    public const string Admin = "Admin";

    /// <summary>Can upload, edit and delete content.</summary>
    public const string Editor = "Editor";

    /// <summary>Read, search and ask — the default for a new account.</summary>
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> All = [Admin, Editor, Viewer];

    public static bool IsKnown(string? role) =>
        role is not null && All.Contains(role, StringComparer.Ordinal);
}
