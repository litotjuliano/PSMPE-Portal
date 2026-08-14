using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Account;
using PSMPE.Portal.Application.Auth;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Authorization;

/// <summary>
/// Real-HTTP coverage of MembershipAccessMiddleware: a fully Expired member is blocked everywhere
/// except an explicit allowlist; grace-period and Active members are unaffected; staff/admin roles
/// (which never have a Member row) are never gated; unauthenticated requests still get a plain 401.
/// </summary>
public class MembershipAccessMiddlewareTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public MembershipAccessMiddlewareTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Registers a brand-new Member-role account (real register + dev-link email verification, so
    /// the resulting token is genuine - same flow as AuthTestHelpers.RegisterAndLoginAsync, but
    /// capturing the user id along the way, which that shared helper discards), then attaches a
    /// Member profile with the given Status/RenewalDueDate directly via the DbContext.
    /// </summary>
    private async Task<string> RegisterMemberWithStatusAsync(MembershipStatus status, DateOnly? renewalDueDate = null)
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var register = await _client.PostAsJsonFromNewClientIpAsync(
            "/api/auth/register", new RegisterRequest(email, "Password123!", "Test Member", DataPrivacyConsent: true));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();

        var (userId, verifyToken) = AuthTestHelpers.ParseVerificationLink(registerBody!.DevVerificationLink!);
        var verify = await _client.PostAsJsonFromNewClientIpAsync(
            "/api/auth/verify-email", new VerifyEmailRequest(userId, verifyToken));
        var verifyBody = await verify.Content.ReadFromJsonAsync<AuthResponse>();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Members.Add(new Member
        {
            UserId = userId,
            FirstName = "Juan",
            LastName = "Dela Cruz",
            Chapter = "NCR",
            MemberType = "Regular",
            Status = status,
            SubmittedAt = DateTimeOffset.UtcNow.AddYears(-1),
            ApprovedAt = DateTimeOffset.UtcNow.AddYears(-1),
            RenewalDueDate = renewalDueDate,
        });
        await db.SaveChangesAsync();

        return verifyBody!.Token;
    }

    private HttpRequestMessage Get(string url, string token) =>
        new HttpRequestMessage(HttpMethod.Get, url).WithBearer(token);

    [Fact]
    public async Task ExpiredMember_IsBlocked_OnANonAllowlistedRoute()
    {
        var token = await RegisterMemberWithStatusAsync(MembershipStatus.Expired, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-100));

        var response = await _client.SendAsync(Get("/api/content", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("MEMBERSHIP_EXPIRED", body!["code"]);
    }

    [Theory]
    [InlineData("/api/members/me")]
    [InlineData("/api/payments/me")]
    [InlineData("/api/payments/fees")]
    public async Task ExpiredMember_IsAllowed_OnAllowlistedRoutes(string url)
    {
        var token = await RegisterMemberWithStatusAsync(MembershipStatus.Expired, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-100));

        var response = await _client.SendAsync(Get(url, token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredMember_CanStillUpdateTheirAccount_ViaTheAllowlistedAccountEndpoint()
    {
        var token = await RegisterMemberWithStatusAsync(MembershipStatus.Expired, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-100));
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/account/me")
        {
            Content = JsonContent.Create(new UpdateAccountRequest("Still Renewing"))
        }.WithBearer(token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GracePeriodMember_IsNotBlocked()
    {
        // Active and within the (7-day) grace window - not yet auto-flipped to Expired.
        var token = await RegisterMemberWithStatusAsync(MembershipStatus.Active, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2));

        var response = await _client.SendAsync(Get("/api/content", token));

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ActiveMember_IsNotBlocked()
    {
        var token = await RegisterMemberWithStatusAsync(MembershipStatus.Active, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(300));

        var response = await _client.SendAsync(Get("/api/content", token));

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StaffRole_WithNoMemberRowAtAll_IsNeverGated()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var (_, token) = await _client.CreatePrivilegedUserAsync(userManager, RoleNames.Admin);

        var response = await _client.SendAsync(Get("/api/content", token));

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedRequest_StillGetsAPlain401_NotInterferedWithByTheMiddleware()
    {
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/content"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
