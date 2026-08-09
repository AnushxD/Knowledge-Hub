using Microsoft.AspNetCore.Identity;

namespace DocHub.DataAccess.Entities;

/// <summary>
/// A person who signs in.
///
/// This is the Identity user store rather than a table beside one, so there is
/// no pair of records that can drift apart — the failure mode being two "users"
/// for one person, only one of whom owns anything.
///
/// Documents and folders no longer point here: the repository owns them, and
/// nobody in the hub does. What remains hanging off a user is what a user
/// genuinely did — chat sessions, and the activity they caused.
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
