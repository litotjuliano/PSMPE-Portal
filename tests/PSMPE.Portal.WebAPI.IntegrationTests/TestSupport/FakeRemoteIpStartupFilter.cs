using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;

/// <summary>
/// TestServer leaves Connection.RemoteIpAddress null, and ForwardedHeaders *skips* its
/// known-peer check entirely when the peer is null - a null peer is maximally trusted, not
/// untrusted. So without this filter the trusted-path tests would pass through that bypass
/// rather than the real KnownNetworks check, and an untrusted-peer test could not be written
/// at all. This stands in for the Docker bridge gateway hop that exists in every real
/// deployment, letting tests drive the genuine forwarded-header code path.
/// </summary>
public class FakeRemoteIpStartupFilter : IStartupFilter
{
    public const string ProxyPeer = "172.17.0.1";
    public const string UntrustedPeer = "203.0.113.250";

    /// <summary>Set per-request via the X-Test-Peer header; defaults to the trusted proxy peer.</summary>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                var peer = context.Request.Headers.TryGetValue("X-Test-Peer", out var value) && !string.IsNullOrWhiteSpace(value)
                    ? value.ToString()
                    : ProxyPeer;
                context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
                await nextMiddleware();
            });
            next(app);
        };
}
