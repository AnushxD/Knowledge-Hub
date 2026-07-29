using DocHub.DataAccess.Entities;
using Hangfire.Dashboard;

namespace DocHub.Api.Infrastructure.Auth;

/// <summary>
/// Lets an administrator into the Hangfire dashboard, and nobody else.
///
/// Hangfire predates endpoint routing and does its own authorisation, so the
/// <c>[Authorize]</c> attributes and the fallback policy do not reach it. This
/// is that gap closed — without it, "/jobs is dev-only" is the only thing
/// standing between a visitor and every queued job's arguments, which include
/// document ids.
/// </summary>
internal sealed class AdminDashboardFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var user = context.GetHttpContext().User;

        return user.Identity?.IsAuthenticated == true && user.IsInRole(Roles.Admin);
    }
}

/// <summary>
/// Middleware that admits only administrators to a path prefix.
///
/// Used for the API browser, which is middleware rather than a routed endpoint
/// and so cannot carry an <c>[Authorize]</c> attribute.
///
/// It answers 404 rather than 403 on purpose: whether this deployment exposes
/// an API explorer at all is not worth confirming to someone who may not know.
/// A signed-out caller never reaches here — the fallback policy applies to
/// requests with no endpoint too, so they get a 401 first.
/// </summary>
internal static class AdminOnlyPathExtensions
{
    public static IApplicationBuilder UseAdminOnly(
        this IApplicationBuilder app,
        params string[] prefixes)
    {
        return app.Use(async (context, next) =>
        {
            var guarded = prefixes.Any(prefix =>
                context.Request.Path.StartsWithSegments(
                    prefix, StringComparison.OrdinalIgnoreCase));

            if (guarded && !(context.User.Identity?.IsAuthenticated == true
                && context.User.IsInRole(Roles.Admin)))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next();
        });
    }
}
