namespace DocHub.Services;

/// <summary>
/// Who is making the current request. Services depend on this rather than on
/// any authentication mechanism, so the host decides what a principal is —
/// ASP.NET Core Identity now, Entra ID later — without business logic moving.
///
/// The implementation is supplied by the host, not by this layer: reading a
/// principal is an HTTP concern, and a Service that knew about HttpContext
/// could not be tested without a web stack.
/// </summary>
public interface ICurrentUser
{
    Guid Id { get; }

    /// <summary>
    /// Admin / Editor / Viewer.
    ///
    /// Exposed here because some rules cannot be expressed on an endpoint:
    /// "delete anyone's document, but only if you are an admin" is a decision
    /// about a particular row, which is business logic and belongs in a
    /// Service. Endpoint-level role checks stay in the controllers.
    /// </summary>
    string Role { get; }

    bool IsAuthenticated { get; }
}
