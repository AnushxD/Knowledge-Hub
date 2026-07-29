using System.Security.Claims;
using DocHub.DataAccess;
using DocHub.DataAccess.Entities;
using DocHub.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
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

        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.Admin, policy => policy.RequireRole(Roles.Admin));

            // Editors and admins may change content; a viewer may not.
            options.AddPolicy(Policies.Contribute, policy =>
                policy.RequireRole(Roles.Admin, Roles.Editor));

            // Everything else still requires *someone* — see the fallback
            // policy applied to controllers, which makes authentication the
            // default rather than something each endpoint must remember.
            options.AddPolicy(Policies.Read, policy => policy.RequireAuthenticatedUser());
        });

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        return services;
    }
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
