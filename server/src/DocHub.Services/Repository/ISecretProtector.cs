using Microsoft.AspNetCore.DataProtection;

namespace DocHub.Services.Repository;

/// <summary>
/// Encrypts the secrets that are stored in the database rather than in
/// configuration — today, the repository's access token and webhook secret.
///
/// A hub-owned interface rather than <c>IDataProtector</c> directly, for two
/// reasons: unprotecting returns null instead of throwing, because a key ring
/// that did not survive a recycle is an ordinary operational event with an
/// obvious fix; and it keeps the Service layer's tests free of the hosting
/// stack.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <summary>
    /// The original secret, or null when it cannot be read — a rotated or lost
    /// key ring. Callers treat that as "no secret is set", which is the only
    /// safe reading: a token nobody can decrypt is a token nobody can use.
    /// </summary>
    string? Unprotect(string ciphertext);
}

/// <summary>
/// ASP.NET Data Protection, keyed by its own purpose so these secrets are not
/// decryptable by anything else the application protects.
///
/// The keys must outlive the process — the same <c>Authentication:KeyPath</c>
/// requirement the session cookie already has. Without it a recycle costs the
/// stored token as well as everyone's session, which is why unreadable
/// ciphertext is reported to an administrator as "set it again" rather than
/// swallowed.
/// </summary>
internal sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector("DocHub.RepositorySettings.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string? Unprotect(string ciphertext)
    {
        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (Exception)
        {
            // Deliberately every exception: the payload is opaque, and every
            // way it can fail to open — a different key ring, a truncated
            // column, a value written by another application — means the same
            // thing to the caller.
            return null;
        }
    }
}
