using DocHub.Api.Infrastructure.Auth;

namespace DocHub.Api.Tests;

/// <summary>
/// The domain allow-list, which is the whole access decision for Google
/// sign-in: past this check the caller gets a session.
///
/// Worth testing directly rather than through the endpoint, because the
/// interesting cases are all string-shaped — a lookalike domain, a subdomain, a
/// second "@" — and none of them need an HTTP request to get wrong.
/// </summary>
public sealed class GoogleDomainTests
{
    private static GoogleOptions AllowingCorp() =>
        new() { Enabled = true, AllowedDomains = ["example-corp.com"] };

    [Theory]
    [InlineData("someone@example-corp.com")]
    [InlineData("Someone@Example-Corp.COM")]
    [InlineData("first.last+tag@example-corp.com")]
    public void A_company_address_is_allowed(string email) =>
        Assert.True(AllowingCorp().IsDomainAllowed(email));

    [Theory]
    // A personal account.
    [InlineData("someone@gmail.com")]
    // The domain as a subdomain of somewhere else — attacker-registrable.
    [InlineData("someone@example-corp.com.evil.test")]
    // A subdomain of ours, which is a different mail domain and not listed.
    [InlineData("someone@mail.example-corp.com")]
    // Lookalikes.
    [InlineData("someone@example-corp.co")]
    [InlineData("someone@examplecorp.com")]
    // The domain appearing anywhere other than after the final "@".
    [InlineData("example-corp.com@evil.test")]
    [InlineData("someone@evil.test?example-corp.com")]
    public void Anything_else_is_refused(string email) =>
        Assert.False(AllowingCorp().IsDomainAllowed(email));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("trailing@")]
    public void A_malformed_address_is_refused(string? email) =>
        Assert.False(AllowingCorp().IsDomainAllowed(email));

    [Fact]
    public void An_empty_allow_list_admits_nobody()
    {
        var options = new GoogleOptions { Enabled = true, AllowedDomains = [] };

        // Failing closed matters more here than anywhere else in the app: the
        // opposite reading of "no domains configured" would turn one missing
        // config value into an open door for every Google account alive.
        Assert.False(options.IsDomainAllowed("someone@example-corp.com"));
        Assert.False(options.IsDomainAllowed("someone@gmail.com"));
    }

    [Fact]
    public void Several_domains_can_be_allowed_at_once()
    {
        var options = new GoogleOptions
        {
            Enabled = true,
            // A leading "@" and stray spacing are the two things someone
            // hand-editing this list actually types.
            AllowedDomains = ["example-corp.com", "@acquired-co.io", " partner.example "],
        };

        Assert.True(options.IsDomainAllowed("a@example-corp.com"));
        Assert.True(options.IsDomainAllowed("b@acquired-co.io"));
        Assert.True(options.IsDomainAllowed("c@partner.example"));
        Assert.False(options.IsDomainAllowed("d@somewhere-else.com"));
    }
}
