using DocHub.DataAccess.Repositories;
using DocHub.Integrations.SourceControl;
using DocHub.Services.Repository;
using Microsoft.Extensions.DependencyInjection;

namespace DocHub.Services.Tests;

/// <summary>
/// Reversible "encryption", so a test can prove a secret was protected on the
/// way in and read back on the way out without a Data Protection key ring.
///
/// The real one is <c>DataProtectionSecretProtector</c>; what the Service layer
/// depends on is the contract, and the behaviour worth testing is which value
/// is stored and when — not the cipher.
/// </summary>
public sealed class FakeSecretProtector : ISecretProtector
{
    private const string Prefix = "protected:";

    /// <summary>Set to make everything unreadable, standing in for a lost key ring.</summary>
    public bool KeysLost { get; set; }

    public string Protect(string plaintext) => Prefix + plaintext;

    public string? Unprotect(string ciphertext)
    {
        if (KeysLost) return null;

        return ciphertext.StartsWith(Prefix, StringComparison.Ordinal)
            ? ciphertext[Prefix.Length..]
            : null;
    }
}

/// <summary>
/// Answers whatever the test scripts, and records what it was asked about — so
/// a test can assert the probe was handed the token already held rather than an
/// empty one.
/// </summary>
public sealed class RecordingConnectionProbe : IRepositoryConnectionProbe
{
    public RepositoryConfiguration? LastCandidate { get; private set; }

    public RepositoryConnection Result { get; set; } = new(
        IsReachable: true, ProjectFound: true, BranchFound: true, SubPathFound: true,
        "Read it.", UsedToken: false);

    public Task<RepositoryConnection> ProbeAsync(
        RepositoryConfiguration candidate,
        CancellationToken ct = default)
    {
        LastCandidate = candidate;
        return Task.FromResult(Result);
    }
}

/// <summary>
/// A scope factory over one already-built repository.
///
/// <c>StoredRepositorySettings</c> is a singleton that opens a scope to read
/// the settings row, exactly as it does in the app. The fixture already owns a
/// DbContext for the "request", so the scope it gets here hands back a
/// repository over that one rather than a second connection.
/// </summary>
public sealed class SingleServiceScopeFactory(IRepositorySettingsRepository settings)
    : IServiceScopeFactory, IServiceScope, IServiceProvider
{
    public IServiceScope CreateScope() => this;

    public IServiceProvider ServiceProvider => this;

    public object? GetService(Type serviceType) =>
        serviceType == typeof(IRepositorySettingsRepository) ? settings : null;

    public void Dispose()
    {
        // The fixture owns the DbContext underneath; disposing it here would
        // close it out from under the rest of the scope's services.
    }
}
