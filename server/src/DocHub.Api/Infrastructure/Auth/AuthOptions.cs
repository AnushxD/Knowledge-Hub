namespace DocHub.Api.Infrastructure.Auth;

/// <summary>
/// Authentication settings, bound from the "Authentication" section.
///
/// Nothing here is a secret except <see cref="GoogleOptions.ClientSecret"/>,
/// which must come from <c>dotnet user-secrets</c> or Key Vault and never from
/// a committed appsettings file.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Authentication";

    /// <summary>
    /// How long a session lasts before the user signs in again. Sliding, so an
    /// active session is not interrupted mid-task.
    /// </summary>
    public int SessionHours { get; set; } = 8;

    /// <summary>
    /// Directory for the Data Protection keys that encrypt the session cookie.
    ///
    /// Set it wherever sessions must survive a restart — notably IIS, where the
    /// default location lives in the application pool's user profile and is
    /// thrown away if that profile is not loaded. Everyone being signed out
    /// after a recycle is what "unset" looks like.
    ///
    /// Left empty the framework default applies, which is the right choice for
    /// a container: the keys are as disposable as the container is.
    /// </summary>
    public string? KeyPath { get; set; }

    public GoogleOptions Google { get; set; } = new();
}

/// <summary>
/// Google sign-in, off unless a deployment turns it on.
///
/// Off by default because it cannot work without credentials: registering the
/// provider regardless would put a button on the login screen that fails on
/// click.
/// </summary>
public sealed class GoogleOptions
{
    public bool Enabled { get; set; }

    public string ClientId { get; set; } = string.Empty;

    /// <summary>Never committed. `dotnet user-secrets set Authentication:Google:ClientSecret …`</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Email domains allowed to sign in, e.g. <c>["example-corp.com"]</c>.
    ///
    /// This is the access gate, so it is enforced on the server against the
    /// email Google returns after the token exchange — never against the
    /// <c>hd</c> hint sent with the authorisation request, which is a
    /// convenience for the account chooser and is trivially altered by whoever
    /// controls the browser.
    ///
    /// Empty means no domain is allowed, not "any domain". A misconfigured
    /// allow-list must fail closed: the alternative turns one missing config
    /// value into an open door for every Google account on the internet.
    /// </summary>
    public string[] AllowedDomains { get; set; } = [];

    /// <summary>
    /// Whether a verified allowed-domain account with no local user gets one
    /// created, as a Viewer.
    ///
    /// On by default when Google is enabled, because the domain check is
    /// already the gate — requiring an admin to pre-create every colleague as
    /// well makes the feature useless for its purpose. Turn it off to run
    /// invitation-only with Google as an authentication method rather than an
    /// entry point.
    /// </summary>
    public bool AutoProvision { get; set; } = true;

    /// <summary>Case-insensitive, trimmed, and tolerant of a leading "@".</summary>
    public bool IsDomainAllowed(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;

        var at = email.LastIndexOf('@');
        if (at < 0 || at == email.Length - 1) return false;

        var domain = email[(at + 1)..].Trim();

        return AllowedDomains.Any(allowed =>
            string.Equals(allowed.TrimStart('@').Trim(), domain, StringComparison.OrdinalIgnoreCase));
    }
}
