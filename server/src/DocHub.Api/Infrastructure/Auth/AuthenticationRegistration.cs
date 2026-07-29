using System.Security.Claims;
using System.Text.Json;
using DocHub.DataAccess;
using DocHub.DataAccess.Entities;
using DocHub.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace DocHub.Api.Infrastructure.Auth;

/// <summary>
/// Authentication and authorisation, composed in one place.
///
/// Identity is used directly rather than wrapped in a Service: it *is* the user
/// store, and a Service layer over it would only forward calls. What the
/// Service layer sees is <see cref="ICurrentUser"/> — one property bag, no
/// framework types — which is the boundary that actually matters.
/// </summary>
internal static class AuthenticationRegistration
{
    public static IServiceCollection AddDocHubAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .Validate(
                options => options.SessionHours > 0,
                "Authentication:SessionHours must be greater than zero.")
            .Validate(
                options => !options.Google.Enabled
                    || !string.IsNullOrWhiteSpace(options.Google.ClientId),
                "Authentication:Google:ClientId is required when Google sign-in is enabled.")
            .Validate(
                options => !options.Google.Enabled
                    || !string.IsNullOrWhiteSpace(options.Google.ClientSecret),
                "Authentication:Google:ClientSecret is required when Google sign-in is enabled. "
                + "Set it with `dotnet user-secrets`, never in an appsettings file.")
            .Validate(
                options => !options.Google.Enabled || options.Google.AllowedDomains.Length > 0,
                "Authentication:Google:AllowedDomains must list at least one domain when Google "
                + "sign-in is enabled. An empty list would otherwise admit every Google account "
                + "in existence.")
            .ValidateOnStart();

        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>()
            ?? new AuthOptions();

        services
            .AddIdentityCore<User>(options =>
            {
                // Length over composition rules. Character-class requirements
                // are what produce "Password1!" everywhere; length is the part
                // that actually costs an attacker something.
                options.Password.RequiredLength = 12;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireDigit = false;

                options.User.RequireUniqueEmail = true;

                // The email is the sign-in name here, so there is no separate
                // username to lose track of.
                options.SignIn.RequireConfirmedAccount = false;

                // Throttles online password guessing. Five attempts then a
                // fifteen-minute wait is slow enough to make brute force
                // pointless without locking out someone with a typo for long.
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<DocHubDbContext>()
            .AddClaimsPrincipalFactory<DocHubClaimsPrincipalFactory>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // Data Protection encrypts the session cookie. Where its keys live
        // decides whether a session survives a restart.
        //
        // The default is a per-user, per-machine location — which under IIS is
        // inside the application pool's profile, and is discarded outright if
        // that profile is not loaded. The symptom is not an error: everybody is
        // simply signed out on the next recycle or deploy, and it reads as an
        // intermittent bug rather than a configuration one.
        //
        // Setting Authentication:KeyPath puts the keys somewhere durable and
        // ends that class of problem. Left unset, the framework default applies
        // — right for a container, where the keys are meant to be as ephemeral
        // as the container.
        var protection = services.AddDataProtection()
            // Fixed, so keys are not re-derived under a different discriminator
            // when the app moves or is renamed.
            .SetApplicationName("DocHub");

        if (!string.IsNullOrWhiteSpace(auth.KeyPath))
        {
            protection.PersistKeysToFileSystem(new DirectoryInfo(auth.KeyPath));
        }

        var authentication = services.AddAuthentication(IdentityConstants.ApplicationScheme);

        authentication.AddCookie(IdentityConstants.ApplicationScheme, options =>
        {
            options.Cookie.Name = "dochub.session";

            // Unreadable from JavaScript, so a cross-site scripting bug cannot
            // walk away with the session.
            options.Cookie.HttpOnly = true;

            // Lax, not Strict: the Google sign-in flow returns via a cross-site
            // redirect, and Strict would drop the cookie on the way back and
            // present a freshly signed-in user with a login page. Lax still
            // blocks the cross-site POST that CSRF depends on, and antiforgery
            // tokens cover what remains.
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

            options.ExpireTimeSpan = TimeSpan.FromHours(auth.SessionHours);
            options.SlidingExpiration = true;

            // This is an API. The browser gets a status code it can act on;
            // redirecting an XHR to a login page returns 200 and an HTML body
            // to code expecting JSON, which is the classic "why is my parser
            // failing" bug.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };

            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };

            // Re-reads the user's security stamp periodically and rejects the
            // cookie if it has moved. Without this, a role change or a disabled
            // account would not take effect until the cookie expired — up to
            // SessionHours of someone keeping access they no longer have.
            options.Events.OnValidatePrincipal = SecurityStampValidator.ValidatePrincipalAsync;
        });

        services.Configure<SecurityStampValidatorOptions>(options =>
        {
            // Five minutes is the usual trade: a revoked permission lingers for
            // at most that long, and a signed-in user costs one extra query per
            // five minutes rather than one per request.
            options.ValidationInterval = TimeSpan.FromMinutes(5);
        });

        // Holds the half-finished identity between leaving for an external
        // provider and coming back. Registered whether or not Google is on, so
        // enabling it is purely a matter of configuration.
        authentication.AddCookie(IdentityConstants.ExternalScheme, options =>
        {
            options.Cookie.Name = "dochub.external";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        });

        // Google is registered only when configured. The alternative — always
        // registering it and failing at sign-in — puts a button on the login
        // screen that breaks when pressed.
        if (auth.Google.Enabled)
        {
            authentication.AddGoogle(options =>
            {
                options.ClientId = auth.Google.ClientId;
                options.ClientSecret = auth.Google.ClientSecret;

                // Where Google returns to. Must match the redirect URI
                // registered in the Google Cloud console exactly.
                options.CallbackPath = "/signin-google";

                // The external cookie holds the identity only until our own
                // callback has checked the domain and issued a real session.
                options.SignInScheme = IdentityConstants.ExternalScheme;

                // Google's default claim mapping drops both of these, and the
                // callback cannot decide anything without the first: an
                // unverified address is one the account merely typed, so
                // trusting it would hand over the domain check itself.
                options.Events.OnCreatingTicket = context =>
                {
                    if (context.Identity is null) return Task.CompletedTask;

                    if (context.User.TryGetProperty("email_verified", out var verified))
                    {
                        context.Identity.AddClaim(new Claim(
                            GoogleClaims.EmailVerified,
                            (verified.ValueKind == JsonValueKind.True).ToString()));
                    }

                    // The hosted domain Google itself asserts, kept for logs —
                    // the access decision uses the verified address, since a
                    // personal account simply has no hd at all.
                    if (context.User.TryGetProperty("hd", out var hostedDomain))
                    {
                        context.Identity.AddClaim(new Claim(
                            GoogleClaims.HostedDomain, hostedDomain.ToString()));
                    }

                    return Task.CompletedTask;
                };

                options.Events.OnRedirectToAuthorizationEndpoint = context =>
                {
                    // Pre-selects the workspace in Google's account chooser
                    // when exactly one domain is allowed. Purely a convenience
                    // — it is a request parameter, so anyone can change it, and
                    // the real check happens on the way back.
                    var uri = auth.Google.AllowedDomains.Length == 1
                        ? QueryHelpers.AddQueryString(
                            context.RedirectUri, "hd", auth.Google.AllowedDomains[0])
                        : context.RedirectUri;

                    context.Response.Redirect(uri);
                    return Task.CompletedTask;
                };
            });
        }

        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.Admin, policy => policy.RequireRole(Roles.Admin));

            // Editors and admins may change content; a viewer may not.
            options.AddPolicy(Policies.Contribute, policy =>
                policy.RequireRole(Roles.Admin, Roles.Editor));

            options.AddPolicy(Policies.Read, policy => policy.RequireAuthenticatedUser());

            // Everything is protected unless it says otherwise. The opposite
            // default — open unless someone remembered an attribute — makes
            // every new endpoint a chance to leak the library, and the mistake
            // is invisible in review because it looks like nothing.
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        return services;
    }
}

/// <summary>Claims copied out of Google's userinfo response.</summary>
internal static class GoogleClaims
{
    /// <summary>"True" only when Google has confirmed the account owns the address.</summary>
    public const string EmailVerified = "urn:google:email_verified";

    /// <summary>The Workspace domain Google asserts, absent for personal accounts.</summary>
    public const string HostedDomain = "urn:google:hd";
}

/// <summary>Named policies, so a string typo cannot quietly weaken an endpoint.</summary>
internal static class Policies
{
    public const string Admin = "dochub.admin";
    public const string Contribute = "dochub.contribute";
    public const string Read = "dochub.read";
}

/// <summary>
/// Puts the user's role and display name into the cookie.
///
/// The role is a column on the user rather than a row in Identity's role
/// tables, so it has to be projected into a claim here for
/// <c>[Authorize(Roles = …)]</c> and the policies above to see it.
/// </summary>
internal sealed class DocHubClaimsPrincipalFactory(
    UserManager<User> users,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<User>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(User user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        identity.AddClaim(new Claim(
            ClaimTypes.Role,
            Roles.IsKnown(user.Role) ? user.Role : Roles.Viewer));

        // Saves the client a round trip for something it shows on every screen.
        identity.AddClaim(new Claim(ClaimTypes.GivenName, user.Name));

        return identity;
    }
}
