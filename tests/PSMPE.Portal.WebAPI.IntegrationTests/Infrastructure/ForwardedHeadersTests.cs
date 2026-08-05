using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Infrastructure;

public class ForwardedHeadersTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ForwardedHeadersTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> AdminTokenAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var (_, token) = await _client.CreatePrivilegedUserAsync(userManager, RoleNames.Admin);
        return token;
    }

    private async Task<string> ResolvedClientIpAsync(string? forwardedFor, string? testPeer = null)
    {
        var token = await AdminTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/diagnostics/client-ip").WithBearer(token);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }
        if (testPeer is not null)
        {
            request.Headers.Add("X-Test-Peer", testPeer);
        }

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return body.GetProperty("clientIp").GetString()!;
    }

    [Fact]
    public async Task ClientIp_FromTrustedProxy_IsTakenFromForwardedHeader()
    {
        Assert.Equal("203.0.113.7", await ResolvedClientIpAsync("203.0.113.7"));
    }

    [Fact]
    public async Task ClientIp_FromUntrustedPeer_IgnoresForwardedHeader()
    {
        var resolved = await ResolvedClientIpAsync("198.51.100.1", FakeRemoteIpStartupFilter.UntrustedPeer);
        Assert.Equal(FakeRemoteIpStartupFilter.UntrustedPeer, resolved);
    }

    [Fact]
    public async Task ClientIp_WithAppendedChain_UsesRightmostEntry()
    {
        // What nginx's $proxy_add_x_forwarded_for actually produces when a client sends its own
        // header: client value first, real peer appended last. Only the last entry is trustworthy.
        Assert.Equal("203.0.113.7", await ResolvedClientIpAsync("198.51.100.1, 203.0.113.7"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    public void BlankKnownNetworksConfig_FallsBackToDefault_RatherThanTrustingEveryPeer(string configured)
    {
        // Regression guard for a fail-open bug. A present-but-empty config value left KnownNetworks
        // empty, and ForwardedHeadersMiddleware only performs its known-peer check when
        // KnownProxies.Count + KnownNetworks.Count > 0 - so an empty set silently trusted
        // X-Forwarded-For from any peer, with no exception and no log. Asserted on the options
        // rather than a resolved IP because the empty set IS the defect - pinning it at the source
        // catches a misconfiguration that a resolved-IP assertion would only catch indirectly.
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ForwardedHeaders:KnownNetworks"] = configured })));

        var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        var network = Assert.Single(options.KnownNetworks);
        Assert.Equal("172.16.0.0", network.Prefix.ToString());
        Assert.Equal(12, network.PrefixLength);
    }

    [Fact]
    public async Task ClientIpDiagnostics_ForNonAdmin_IsRefused()
    {
        var memberToken = await _client.RegisterAndLoginAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/diagnostics/client-ip")
            .WithBearer(memberToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
