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

    [Fact]
    public async Task TrippingTheLockoutThreshold_WritesExactlyOneAuditLogRow()
    {
        using var setupScope = _factory.Services.CreateScope();
        var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{Guid.NewGuid()}@example.com";
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = "Lockout Test", EmailConfirmed = true };
        await userManager.CreateAsync(user, "Password123!");

        // Default MaxFailedAccessAttempts is 5 - see DependencyInjection.cs's Lockout options.
        for (var i = 0; i < 5; i++)
        {
            var ip = UniqueIp();
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new LoginRequest(email, "WrongPassword!"))
            };
            request.Headers.Add("X-Forwarded-For", ip);
            await _client.SendAsync(request);
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.AuditLogs, a => a.EventType == "auth.account.locked_out");
        Assert.Equal(user.Id, row.ActorUserId);

        // A follow-up attempt against the now-locked account must not write a second row.
        var extra = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new LoginRequest(email, "WrongPassword!"))
        };
        extra.Headers.Add("X-Forwarded-For", UniqueIp());
        await _client.SendAsync(extra);
        Assert.Single(db.AuditLogs, a => a.EventType == "auth.account.locked_out");
    }

    [Fact]
    public async Task ExceedingTheEmailSendThrottle_WritesAuditLogRow()
    {
        using var setupScope = _factory.Services.CreateScope();
        var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{Guid.NewGuid()}@example.com";
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = "Throttle Test", EmailConfirmed = true };
        await userManager.CreateAsync(user, "Password123!");

        // Default RateLimit:EmailSendPerAddress:PermitLimit is 3 - see MemoryCacheEmailSendThrottle.
        for (var i = 0; i < 4; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgot-password")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new ForgotPasswordRequest(email))
            };
            request.Headers.Add("X-Forwarded-For", UniqueIp());
            await _client.SendAsync(request);
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.AuditLogs, a => a.EventType == "auth.email_throttle.blocked");
        Assert.Contains(email, row.Metadata);
    }
}
