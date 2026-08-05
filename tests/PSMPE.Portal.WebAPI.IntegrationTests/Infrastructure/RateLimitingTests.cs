using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using PSMPE.Portal.Application.Auth;
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
    /// Limiter state is process-wide and the factory is shared across the class, so every test
    /// must claim its own partition or tests pollute each other's counters and fail by run order.
    /// </summary>
    private static string UniqueIp() => $"198.51.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 255)}";

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
