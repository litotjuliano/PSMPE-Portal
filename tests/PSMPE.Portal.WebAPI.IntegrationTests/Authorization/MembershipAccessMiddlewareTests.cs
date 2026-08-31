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
/// Also covers the independent portal-access check: an Active member lacking portal access is
/// blocked with PORTAL_ACCESS_REQUIRED outside the allowlist (Events browsing is a deliberate
/// exception - GetAll/GetById/GetPoster carry [AllowExpiredMember] so any member can browse
/// regardless, only Register itself requires portal access), a member failing both checks sees
/// MEMBERSHIP_EXPIRED (the expiry check runs first), and a Deactivated member is exempt from the
/// portal-access check. And the third, independent check: a "Member"-role account with no Member
/// row at all yet (registered but never submitted an application) is blocked with
/// MEMBERSHIP_NOT_STARTED outside the allowlist, while still reaching the allowlisted
/// /api/members/me endpoint (which reports 404, not the middleware's 403).
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
    /// hasPortalAccess defaults to true - these tests are about the expiry axis, not the portal-access
    /// axis, so defaulting it true keeps every existing call site's intent unchanged. Tests targeting
    /// the portal-access check pass hasPortalAccess: false explicitly.
    /// </summary>
    private async Task<string> RegisterMemberWithStatusAsync(
        MembershipStatus status, DateOnly? renewalDueDate = null, bool hasPortalAccess = true)
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
            HasPortalAccess = hasPortalAccess,
        });
        await db.SaveChangesAsync();

        return verifyBody!.Token;
    }

    private HttpRequestMessage Get(string url, string token) =>
        new HttpRequestMessage(HttpMethod.Get, url).WithBearer(token);

    /// <summary>
    /// Real self-registration (account + "Member" role), deliberately never followed by a Member
    /// row - reproduces the account state AuthController.Register alone always produces, before
    /// MemberService.SubmitMyProfileAsync ever runs. Found via a live account
    /// (andreisabaterTest@gmail.com on staging) that could reach event registration with no
    /// membership application submitted at all.
    /// </summary>
    private async Task<string> RegisterMemberWithNoProfileAsync()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var register = await _client.PostAsJsonFromNewClientIpAsync(
            "/api/auth/register", new RegisterRequest(email, "Password123!", "No Profile Yet", DataPrivacyConsent: true));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();

        var (userId, verifyToken) = AuthTestHelpers.ParseVerificationLink(registerBody!.DevVerificationLink!);
        var verify = await _client.PostAsJsonFromNewClientIpAsync(
            "/api/auth/verify-email", new VerifyEmailRequest(userId, verifyToken));
        var verifyBody = await verify.Content.ReadFromJsonAsync<AuthResponse>();

        return verifyBody!.Token;
    }

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

    [Fact]
    public async Task MemberLackingPortalAccess_IsBlocked_OnANonAllowlistedRoute()
    {
        // Active, not Expired - isolates the portal-access axis from the expiry axis.
        var token = await RegisterMemberWithStatusAsync(
            MembershipStatus.Active, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(300), hasPortalAccess: false);

        var response = await _client.SendAsync(Get("/api/content", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("PORTAL_ACCESS_REQUIRED", body!["code"]);
    }

    [Fact]
    public async Task MemberLackingPortalAccess_CanStillBrowseEvents()
    {
        // Events is meant to be browsable by every member regardless of the portal-access add-on -
        // GetAll/GetById/GetPoster carry [AllowExpiredMember] specifically for this. Only the
        // Register action itself (below) requires portal access.
        var token = await RegisterMemberWithStatusAsync(
            MembershipStatus.Active, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(300), hasPortalAccess: false);

        var response = await _client.SendAsync(Get("/api/events", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MemberLackingPortalAccess_IsBlocked_FromRegisteringForAnEvent()
    {
        // Register carries no [AllowExpiredMember] - browsing is open (see the test above), but
        // actually registering still requires portal access.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var @event = new Event
        {
            Title = "Portal Access Gate Test Event",
            StartsAt = DateTimeOffset.UtcNow.AddDays(10),
            EndsAt = DateTimeOffset.UtcNow.AddDays(10).AddHours(4),
            Status = EventStatus.Published,
        };
        db.Events.Add(@event);
        await db.SaveChangesAsync();

        var token = await RegisterMemberWithStatusAsync(
            MembershipStatus.Active, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(300), hasPortalAccess: false);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/events/{@event.Id}/register")
        {
            Content = JsonContent.Create(new { mode = "Onsite" })
        }.WithBearer(token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("PORTAL_ACCESS_REQUIRED", body!["code"]);
    }

    [Theory]
    [InlineData("/api/members/me")]
    [InlineData("/api/payments/me")]
    [InlineData("/api/payments/fees")]
    public async Task MemberLackingPortalAccess_IsAllowed_OnAllowlistedRoutes(string url)
    {
        var token = await RegisterMemberWithStatusAsync(
            MembershipStatus.Active, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(300), hasPortalAccess: false);

        var response = await _client.SendAsync(Get(url, token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MemberBothExpiredAndLackingPortalAccess_SeesMembershipExpired_NotPortalAccessRequired()
    {
        // The expiry check runs first, per proposal.md's ordering rule - a member failing both
        // conditions sees the expiry message, not the portal-access one.
        var token = await RegisterMemberWithStatusAsync(
            MembershipStatus.Expired, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-100), hasPortalAccess: false);

        var response = await _client.SendAsync(Get("/api/content", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("MEMBERSHIP_EXPIRED", body!["code"]);
    }

    [Fact]
    public async Task DeactivatedMemberLackingPortalAccess_IsNotBlocked_ByThePortalAccessCheck()
    {
        // Deactivated is its own admin action, excluded from this check the same way it's excluded
        // from ComputeIsExpired/ComputeIsInGracePeriod.
        var token = await RegisterMemberWithStatusAsync(MembershipStatus.Deactivated, hasPortalAccess: false);

        var response = await _client.SendAsync(Get("/api/content", token));

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MemberRoleWithNoProfileAtAll_IsBlocked_OnANonAllowlistedRoute()
    {
        var token = await RegisterMemberWithNoProfileAsync();

        var response = await _client.SendAsync(Get("/api/content", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("MEMBERSHIP_NOT_STARTED", body!["code"]);
    }

    [Fact]
    public async Task MemberRoleWithNoProfileAtAll_CanStillReachTheAllowlistedMeEndpoint_WhichReportsNotFound()
    {
        // The middleware doesn't block /api/members/me (it's [AllowExpiredMember]) - it's the
        // controller itself that correctly reports "no profile" here, exactly as it would for any
        // other caller with no Member row. Confirms the new MEMBERSHIP_NOT_STARTED check isn't
        // accidentally blocking the one endpoint a pre-application member needs to discover that
        // fact and get routed to the application wizard.
        var token = await RegisterMemberWithNoProfileAsync();

        var response = await _client.SendAsync(Get("/api/members/me", token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
