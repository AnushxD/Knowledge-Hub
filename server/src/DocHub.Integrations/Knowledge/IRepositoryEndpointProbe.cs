using Microsoft.Extensions.Logging;

namespace DocHub.Integrations.Knowledge;

/// <summary>
/// Answers whether an address responds at all, before an administrator commits
/// to it.
///
/// Deliberately weaker than a health check, and worded that way everywhere it
/// surfaces: it establishes that something is listening and reachable, which
/// catches the mistakes people actually make — a typo, the wrong port, a
/// firewall in the way. It cannot establish that the thing listening speaks
/// MCP, and claiming otherwise would be worse than saying nothing.
/// </summary>
public interface IRepositoryEndpointProbe
{
    Task<EndpointProbeResult> ProbeAsync(string endpoint, CancellationToken ct = default);
}

/// <param name="Detail">Plain wording for the administrator, naming what failed when it did.</param>
public sealed record EndpointProbeResult(bool IsReachable, string Detail);

internal sealed class HttpRepositoryEndpointProbe(
    HttpClient http,
    ILogger<HttpRepositoryEndpointProbe> logger) : IRepositoryEndpointProbe
{
    public async Task<EndpointProbeResult> ProbeAsync(
        string endpoint,
        CancellationToken ct = default)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return new EndpointProbeResult(false, "That is not a valid absolute URL.");

        try
        {
            // Any HTTP response counts, including 404 or 401. The question is
            // "did something answer", not "did it like this request" — an MCP
            // server has no reason to serve anything at its root.
            using var response = await http.GetAsync(
                uri, HttpCompletionOption.ResponseHeadersRead, ct);

            return new EndpointProbeResult(
                true,
                $"Reachable — answered {(int)response.StatusCode} {response.ReasonPhrase}. "
                + "This confirms the address and the network path, not that the server speaks MCP.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new EndpointProbeResult(
                false, "Timed out. The host may be firewalled or the port wrong.");
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Probe of {Endpoint} failed", uri);

            return new EndpointProbeResult(false, $"Could not connect ({exception.Message}).");
        }
    }
}
