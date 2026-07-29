using DocHub.Api.Infrastructure.Auth;
using DocHub.DataAccess.Entities;
using DocHub.Services.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DocHub.Api.Controllers;

/// <summary>
/// Signing in and out, and asking who you are.
///
/// Talks to Identity directly rather than through a Service. Identity is the
/// user store, and a Service that only forwarded to <c>SignInManager</c> would
/// be a layer with no decisions in it — the business logic these endpoints
/// carry is which failures the caller is allowed to tell apart, which is
/// stated below.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(
    SignInManager<User> signIn,
    UserManager<User> users,
    IOptions<AuthOptions> options,
    ILogger<AuthController> logger) : ControllerBase
{
    private readonly AuthOptions options = options.Value;

    /// <summary>What sign-in methods this deployment offers.</summary>
    [HttpGet("options")]
    [AllowAnonymous]
    [ProducesResponseType<AuthOptionsViewModel>(StatusCodes.Status200OK)]
    public AuthOptionsViewModel Options() => new(options.Google.Enabled);

    /// <summary>Signs in with an email and password, issuing the session cookie.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<SignedInUserViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SignedInUserViewModel>> Login([FromBody] LoginRequest request)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var user = await users.FindByEmailAsync(email);

        if (user is not null)
        {
            var result = await signIn.PasswordSignInAsync(
                user, request.Password ?? string.Empty, isPersistent: true, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                logger.LogInformation("User {UserId} signed in with a password", user.Id);
                return Describe(user);
            }

            if (result.IsLockedOut)
            {
                logger.LogWarning("Sign-in refused for {UserId}: account is locked out", user.Id);

                // Worth telling the truth about: the user cannot fix this by
                // typing more carefully, and an attacker who triggered the
                // lockout already knows they did.
                return Problem(
                    title: "Account locked",
                    detail: "Too many failed attempts. Try again in 15 minutes.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }
        }

        // One message for "no such account" and for "wrong password", and the
        // same work done either way. Distinguishing them turns the login form
        // into a tool for discovering who has an account here.
        logger.LogWarning("Failed sign-in attempt for {Email}", email);

        return Problem(
            title: "Sign-in failed",
            detail: "That email and password do not match an account.",
            statusCode: StatusCodes.Status401Unauthorized);
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return NoContent();
    }

    /// <summary>
    /// The current user, or 401. The client calls this on load to decide
    /// between the app and the login screen.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<SignedInUserViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SignedInUserViewModel>> Me()
    {
        var user = await users.GetUserAsync(User);

        // The cookie is valid but its user is gone — deleted mid-session.
        // Signing the cookie out here stops it presenting the same puzzle on
        // every request.
        if (user is null)
        {
            await signIn.SignOutAsync();
            return Unauthorized();
        }

        return Describe(user);
    }

    private static SignedInUserViewModel Describe(User user) =>
        new(
            user.Id,
            user.Name,
            user.Email ?? string.Empty,
            Initials(user.Name),
            Roles.IsKnown(user.Role) ? user.Role : Roles.Viewer);

    /// <summary>Mirrors the avatar initials the rest of the API returns.</summary>
    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
        };
    }
}
