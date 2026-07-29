using System.Security.Claims;
using DocHub.DataAccess.Entities;
using DocHub.Services;

namespace DocHub.Api.Infrastructure.Auth;

/// <summary>
/// The authenticated principal, as the Service layer sees it.
///
/// This is the phase 5 replacement for the seeded stand-in, and the reason
/// <see cref="ICurrentUser"/> existed from phase 1: every service already
/// attributes ownership to <c>currentUser.Id</c>, so real authentication is one
/// registration rather than a change to business logic.
///
/// It lives in the API project because it reads <c>HttpContext</c> — the same
/// arrangement as <c>HangfireIngestionQueue</c>, where Services defines the
/// interface and the host supplies the mechanism.
/// </summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid Id => TryGetId()
        // Reached only if a service runs outside an authenticated request.
        // Throwing beats falling back to a default identity: silently
        // attributing someone's upload to a system account is a data-integrity
        // bug that would surface much later, as a document nobody owns.
        ?? throw new InvalidOperationException(
            "No authenticated user on this request. A background job must supply its own "
            + "ICurrentUser rather than resolving the request-scoped one.");

    public string Role =>
        accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role) ?? Roles.Viewer;

    public bool IsAuthenticated => TryGetId() is not null;

    private Guid? TryGetId()
    {
        var value = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var id) ? id : null;
    }
}
