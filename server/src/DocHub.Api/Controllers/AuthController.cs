using System.Security.Claims;
using DocHub.Api.Infrastructure.Auth;
using DocHub.DataAccess.Entities;
using DocHub.Services.ViewModels;
using Microsoft.AspNetCore.Authentication.Google;
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

    /// <summary>
    /// Starts Google sign-in. The browser is handed to Google and comes back at
    /// <see cref="GoogleCallback"/>.
    /// </summary>
    [HttpGet("google/start")]
    [AllowAnonymous]
    public IActionResult GoogleStart([FromQuery] string? returnUrl)
    {
        if (!options.Google.Enabled) return NotFound();

        var properties = signIn.ConfigureExternalAuthenticationProperties(
            GoogleDefaults.AuthenticationScheme,
            Url.Action(nameof(GoogleCallback), new { returnUrl = SafeReturnUrl(returnUrl) }));

        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Where Google sends the user back, and where access is actually decided.
    ///
    /// Every check here runs against the identity Google returned from the
    /// token exchange, never against anything the browser supplied: the `hd`
    /// hint on the way out is a convenience for the account chooser and can be
    /// edited by whoever controls the URL bar. Treating it as the gate would
    /// let any Google account in the world sign in.
    /// </summary>
    [HttpGet("google/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleCallback([FromQuery] string? returnUrl)
    {
        if (!options.Google.Enabled) return NotFound();

        var target = SafeReturnUrl(returnUrl);
        var info = await signIn.GetExternalLoginInfoAsync();

        if (info is null)
        {
            logger.LogWarning("Google callback reached with no external login information");
            return Redirect($"/login?error=external");
        }

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);

        // Google will happily assert an address the account has not proved it
        // owns. An unverified address is attacker-choosable, so treating one as
        // a company address would hand over the domain check itself.
        var emailVerified = string.Equals(
            info.Principal.FindFirstValue(GoogleClaims.EmailVerified),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(email) || !emailVerified)
        {
            logger.LogWarning(
                "Google sign-in refused: address missing or unverified for {LoginProvider} key",
                info.LoginProvider);

            return Redirect("/login?error=unverified");
        }

        if (!options.Google.IsDomainAllowed(email))
        {
            // Logged as a refusal rather than an error: someone signing in with
            // a personal account is the expected case this exists to stop.
            logger.LogWarning("Google sign-in refused for {Email}: domain not allowed", email);
            return Redirect("/login?error=domain");
        }

        var user = await users.FindByEmailAsync(email);

        if (user is null)
        {
            if (!options.Google.AutoProvision)
            {
                logger.LogWarning(
                    "Google sign-in refused for {Email}: no account, and auto-provisioning is off",
                    email);

                return Redirect("/login?error=noaccount");
            }

            user = await ProvisionAsync(email, info);

            if (user is null) return Redirect("/login?error=provision");
        }

        // Records the Google identity against the account, so a later email
        // change on our side does not orphan the external login.
        if (await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey) is null)
        {
            await users.AddLoginAsync(user, info);
        }

        await signIn.SignInAsync(user, isPersistent: true);

        logger.LogInformation("User {UserId} signed in with Google", user.Id);

        return Redirect(target);
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

    /// <summary>
    /// Creates a local account for a verified, allowed-domain Google identity.
    ///
    /// Always a Viewer. The domain proves who someone works for, not what they
    /// should be able to change — an admin promotes them afterwards, so a new
    /// colleague's first sign-in can never hand out write access on its own.
    /// </summary>
    private async Task<User?> ProvisionAsync(string email, ExternalLoginInfo info)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            Name = info.Principal.FindFirstValue(ClaimTypes.Name)?.Trim() is { Length: > 0 } name
                ? name
                : email,
            Role = Roles.Viewer,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // No password: this account signs in through Google, and a local
        // credential nobody set is one more thing that could be guessed.
        var created = await users.CreateAsync(user);

        if (created.Succeeded)
        {
            logger.LogInformation(
                "Provisioned {UserId} as {Role} from a verified Google sign-in",
                user.Id, user.Role);

            return user;
        }

        logger.LogError(
            "Could not provision an account for a Google sign-in: {Errors}",
            string.Join("; ", created.Errors.Select(error => error.Description)));

        return null;
    }

    /// <summary>
    /// Keeps the post-sign-in redirect inside this application.
    ///
    /// Without this the endpoint is an open redirect: a link to
    /// <c>…/google/start?returnUrl=https://evil.example</c> would send a user
    /// who genuinely just authenticated onward to somebody else's site.
    /// </summary>
    private string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";

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
