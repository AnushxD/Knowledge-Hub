using DocHub.DataAccess;

namespace DocHub.Services;

/// <summary>
/// Who is making the current request. Services depend on this rather than on
/// any authentication mechanism, so phase 5 can swap in a real principal
/// without touching business logic.
/// </summary>
public interface ICurrentUser
{
    Guid Id { get; }
}

/// <summary>
/// Phase 1 stand-in: everything is attributed to the seeded local development
/// user. Replaced in phase 5 by an implementation reading the authenticated
/// principal (ASP.NET Core Identity, then Entra ID).
/// </summary>
internal sealed class SeededCurrentUser : ICurrentUser
{
    public Guid Id => DocHubDbContext.SystemUserId;
}
