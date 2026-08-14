using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Auth;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Infrastructure;

public class AuditLogWritingTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuditLogWritingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static string UniqueIp() => AuthTestHelpers.NextClientIp(secondOctet: 21);

    [Fact]
    public async Task ExceedingLoginRateLimit_WritesAuditLogRow()
    {
        var ip = UniqueIp();
        for (var i = 0; i < 21; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new LoginRequest($"{Guid.NewGuid()}@example.com", "Password123!"))
            };
            request.Headers.Add("X-Forwarded-For", ip);
            await _client.SendAsync(request);
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.AuditLogs, a => a.ActorIp == ip);
        Assert.Equal("auth.rate_limit.rejected", row.EventType);
        Assert.Null(row.ActorUserId);
        Assert.Contains("auth-ip", row.Metadata);
    }

    [Fact]
    public async Task GlobalCeilingRejectingAnAlreadyAuthenticatedCaller_StillAuditsWithNullActor()
    {
        // ActorUserId is always null for this event type, even for an already-logged-in caller -
        // app.UseRateLimiter() runs before app.UseAuthentication() in Program.cs, deliberately
        // (see its comment: the global ceiling has to protect the auth surface itself, not just
        // requests that already passed it), so no authenticated identity is ever available yet
        // at the point a rejection occurs. This test's bearer token proves the caller COULD have
        // authenticated - proving ActorUserId is still null despite that.
        using var setupScope = _factory.Services.CreateScope();
        var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var (_, token) = await _client.CreatePrivilegedUserAsync(userManager, RoleNames.Admin);

        var ip = UniqueIp();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 301; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/roles").WithBearer(token);
            request.Headers.Add("X-Forwarded-For", ip);
            last = await _client.SendAsync(request);
        }

        Assert.Equal(System.Net.HttpStatusCode.TooManyRequests, last!.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.AuditLogs, a => a.ActorIp == ip);
        Assert.Null(row.ActorUserId);
        Assert.Contains("global", row.Metadata);
    }
}
