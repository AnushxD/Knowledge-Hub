using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace DocHub.Api.Infrastructure;

/// <summary>
/// Limits on the endpoints that cost something to serve.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>Questions one user may ask per <see cref="ChatWindowSeconds"/>.</summary>
    public int ChatRequests { get; set; } = 10;

    public int ChatWindowSeconds { get; set; } = 60;
}

internal static class RateLimiting
{
    /// <summary>Policy name, referenced by the attribute on the chat endpoint.</summary>
    public const string ChatPolicy = "chat";

    /// <summary>
    /// Rate limiting for generation.
    ///
    /// Asking a question is the only endpoint here that occupies a model for
    /// several seconds, so it is the only one where a handful of clients can
    /// exhaust the service for everyone else — with a local model serving one
    /// request at a time, "a handful" is a low number. Reads are cheap and left
    /// alone.
    ///
    /// Partitioned per user rather than per IP: everyone behind an office NAT
    /// shares an address, and a limit they collectively trip is a limit that
    /// punishes the wrong people.
    /// </summary>
    public static IServiceCollection AddDocHubRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .Validate(
                options => options.ChatRequests > 0,
                "RateLimits:ChatRequests must be greater than zero.")
            .Validate(
                options => options.ChatWindowSeconds > 0,
                "RateLimits:ChatWindowSeconds must be greater than zero.")
            .ValidateOnStart();

        var limits = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
            ?? new RateLimitOptions();

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(ChatPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.ChatRequests,
                    Window = TimeSpan.FromSeconds(limits.ChatWindowSeconds),
                    // Rejected outright rather than queued. A queued question
                    // is one the user watches spin with no way to tell it from
                    // a slow answer, and the honest response is "you are asking
                    // faster than this can answer".
                    QueueLimit = 0,
                }));

            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                // Written as problem details to match every other error this
                // API returns, so the client has one error shape to handle.
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    type = "https://tools.ietf.org/html/rfc9110#section-15.5.29",
                    title = "Too many questions",
                    status = StatusCodes.Status429TooManyRequests,
                    detail = "You are asking faster than the assistant can answer. "
                        + "Wait a moment and try again.",
                }, ct);
            };
        });

        return services;
    }
}
