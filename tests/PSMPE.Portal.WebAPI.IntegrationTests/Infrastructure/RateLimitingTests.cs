using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using PSMPE.Portal.Application.Auth;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Infrastructure;

public class RateLimitingTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RateLimitingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// The factory is an IClassFixture, so this class gets its own host, DI container and limiter
    /// - state is not shared with other test classes. It is shared by every test in *this* class
    /// though, so each must claim its own partition or they pollute each other's counters and
    /// start failing by run order. Same sequential source as the rest of the suite, in a visibly
    /// separate range so an address in a failure message points back here.
    /// </summary>
    private static string UniqueIp() => AuthTestHelpers.NextClientIp(secondOctet: 19);

    private async Task<HttpResponseMessage> PostLoginAsync(string clientIp)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest($"{Guid.NewGuid()}@example.com", "Password123!"))
        };
        request.Headers.Add("X-Forwarded-For", clientIp);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Login_WithinTheLimit_IsNotThrottled()
    {
        var ip = UniqueIp();
        for (var i = 0; i < 20; i++)
        {
            var response = await PostLoginAsync(ip);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Fact]
    public async Task Login_BeyondTheLimit_Returns429WithProblemDetailsAndRetryAfter()
    {
        var ip = UniqueIp();
        for (var i = 0; i < 20; i++)
        {
            await PostLoginAsync(ip);
        }

        var response = await PostLoginAsync(ip);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.Contains("Retry-After"), "Retry-After header must be present");

        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(429, body.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Login_LimitsArePartitionedByClientIp()
    {
        var exhausted = UniqueIp();
        for (var i = 0; i < 21; i++)
        {
            await PostLoginAsync(exhausted);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostLoginAsync(exhausted)).StatusCode);

        var fresh = await PostLoginAsync(UniqueIp());
        Assert.NotEqual(HttpStatusCode.TooManyRequests, fresh.StatusCode);
    }

    [Fact]
    public async Task UsernameAvailable_ToleratesTypeaheadVolume()
    {
        var ip = UniqueIp();
        for (var i = 0; i < 15; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/username-available?username=probe{i}");
            request.Headers.Add("X-Forwarded-For", ip);
            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    /// <summary>
    /// The global limiter partitions on client IP and would otherwise count liveness probes
    /// against the same 300/min bucket as real traffic. That only bites once something else is
    /// already wrong - a broken X-Forwarded-For chain collapses every caller into one partition,
    /// or a flood fills it - and then it becomes a loop: probes start getting 429s, the container
    /// is marked unhealthy, restart: unless-stopped restarts it, counters reset, repeat.
    /// Deliberately well past the global limit, all from one fixed IP.
    /// </summary>
    [Fact]
    public async Task HealthCheck_IsNeverRateLimited()
    {
        var ip = UniqueIp();
        for (var i = 0; i < 320; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/health");
            request.Headers.Add("X-Forwarded-For", ip);
            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private async Task<HttpResponseMessage> PostForgotPasswordAsync(string clientIp)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgot-password")
        {
            // A fresh address each time, so nothing here depends on the account existing - the
            // endpoint answers identically either way, by design.
            Content = JsonContent.Create(new ForgotPasswordRequest($"{Guid.NewGuid()}@example.com"))
        };
        request.Headers.Add("X-Forwarded-For", clientIp);
        return await _client.SendAsync(request);
    }

    /// <summary>
    /// auth-email-send is the tightest limit on the surface and the one standing between a
    /// stranger and a member's inbox, so it is covered on its own rather than assumed to work
    /// because auth-ip does. One fixed client IP throughout: the claim being tested is that a
    /// single caller cannot keep asking for sends.
    /// </summary>
    [Fact]
    public async Task ForgotPassword_BeyondTheEmailSendLimit_IsThrottled()
    {
        var ip = UniqueIp();
        for (var i = 0; i < 10; i++)
        {
            var response = await PostForgotPasswordAsync(ip);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        var throttled = await PostForgotPasswordAsync(ip);

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
    }

    [Fact]
    public async Task RateLimiting_WhenDisabled_NeverThrottles()
    {
        // The kill switch has to actually kill: if limiting ever throttles the wrong people in
        // production, flipping this env var is the rollback, so it needs to be known to work.
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimit:Enabled"] = "false"
                })));
        using var client = factory.CreateClient();

        var ip = UniqueIp();
        for (var i = 0; i < 25; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest($"{Guid.NewGuid()}@example.com", "Password123!"))
            };
            request.Headers.Add("X-Forwarded-For", ip);
            var response = await client.SendAsync(request);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }
}
