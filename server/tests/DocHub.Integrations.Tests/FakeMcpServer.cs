using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DocHub.Integrations.Tests;

/// <summary>An MCP server on a loopback port, torn down with the test.</summary>
internal sealed class FakeMcpServer : IAsyncDisposable
{
    private readonly WebApplication app;

    private FakeMcpServer(WebApplication app, string endpoint)
    {
        this.app = app;
        Endpoint = endpoint;
    }

    public string Endpoint { get; }

    public static Task<FakeMcpServer> StartAsync() => StartAsync<FakeRepositoryTools>();

    /// <summary>
    /// Hosts a chosen set of tools, so a server with nothing searchable on it
    /// is as easy to stand up as the ordinary one — that case has its own
    /// reporting and would otherwise go untested.
    /// </summary>
    public static async Task<FakeMcpServer> StartAsync<TTools>()
        where TTools : class
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Port 0: the OS picks a free one, so parallel test runs cannot
        // collide on a hard-coded port.
        builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<TTools>();

        var app = builder.Build();
        app.MapMcp();

        await app.StartAsync();

        var address = app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()!
            .Addresses
            .First();

        return new FakeMcpServer(app, address);
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
