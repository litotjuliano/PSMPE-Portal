using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Auth;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Events;

/// <summary>
/// Exercises the Event Management / CPD Tracker endpoints via real HTTP - authorization gating on
/// the admin-only actions, and one full member-side round trip (register, pay, get verified, get
/// attendance recorded, evaluate, read CPD, download a certificate) exercising the whole state
/// machine end to end. See add-events-cpd-tracker/specs/events/spec.md.
/// </summary>
public class EventsControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly HttpClient _client;

    public EventsControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _scope = factory.Services.CreateScope();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        _scope.Dispose();
        return Task.CompletedTask;
    }

    private Task<(Guid UserId, string Token)> CreateAdminAsync() =>
        _client.CreatePrivilegedUserAsync(_userManager, RoleNames.Admin);

    /// <summary>
    /// Registers a brand-new Member-role account (real register + dev-link email verification -
    /// same flow as AuthTestHelpers.RegisterAndLoginAsync), then attaches an Active Member profile
    /// directly via the DbContext. EventService.RegisterAsync requires a genuine Member row (it
    /// looks one up by UserId), which the bare auth flow alone never creates - registering an
    /// ApplicationUser and completing a membership application are deliberately separate steps in
    /// this system, so tests exercising event registration need to skip straight to "already an
    /// approved member" the same way MembershipAccessMiddlewareTests does. HasPortalAccess is set
    /// true - these tests exercise event registration over real HTTP (so MembershipAccessMiddleware's
    /// portal-access check does apply), not the portal-access axis itself, which
    /// MembershipAccessMiddlewareTests covers directly.
    /// </summary>
    private async Task<string> RegisterMemberAsync()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var register = await _client.PostAsJsonFromNewClientIpAsync(
            "/api/auth/register", new RegisterRequest(email, "Password123!", "Test Member", DataPrivacyConsent: true));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();

        var (userId, verifyToken) = AuthTestHelpers.ParseVerificationLink(registerBody!.DevVerificationLink!);
        var verify = await _client.PostAsJsonFromNewClientIpAsync(
            "/api/auth/verify-email", new VerifyEmailRequest(userId, verifyToken));
        var verifyBody = await verify.Content.ReadFromJsonAsync<AuthResponse>();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Members.Add(new Member
            {
                UserId = userId,
                FirstName = "Test",
                LastName = "Member",
                Chapter = Chapters.Ncr,
                MemberType = "Regular",
                Status = MembershipStatus.Active,
                SubmittedAt = DateTimeOffset.UtcNow.AddYears(-1),
                ApprovedAt = DateTimeOffset.UtcNow.AddYears(-1),
                HasPortalAccess = true,
            });
            await db.SaveChangesAsync();
        }

        return verifyBody!.Token;
    }

    /// <summary>Same as RegisterMemberAsync but lacking the portal-access add-on, for the browse-
    /// vs-register distinction test below - MembershipAccessMiddlewareTests covers the axis itself
    /// generically, this confirms Events' own endpoints actually carry the split correctly.</summary>
    private async Task<string> RegisterMemberLackingPortalAccessAsync()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var register = await _client.PostAsJsonFromNewClientIpAsync(
            "/api/auth/register", new RegisterRequest(email, "Password123!", "Test Member", DataPrivacyConsent: true));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();

        var (userId, verifyToken) = AuthTestHelpers.ParseVerificationLink(registerBody!.DevVerificationLink!);
        var verify = await _client.PostAsJsonFromNewClientIpAsync(
            "/api/auth/verify-email", new VerifyEmailRequest(userId, verifyToken));
        var verifyBody = await verify.Content.ReadFromJsonAsync<AuthResponse>();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Members.Add(new Member
            {
                UserId = userId,
                FirstName = "Test",
                LastName = "Member",
                Chapter = Chapters.Ncr,
                MemberType = "Regular",
                Status = MembershipStatus.Active,
                SubmittedAt = DateTimeOffset.UtcNow.AddYears(-1),
                ApprovedAt = DateTimeOffset.UtcNow.AddYears(-1),
                HasPortalAccess = false,
            });
            await db.SaveChangesAsync();
        }

        return verifyBody!.Token;
    }

    private HttpRequestMessage PostJson(string url, object body, string token) =>
        new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) }.WithBearer(token);

    private static object ValidEventPayload(string title = "Water Sanitation Workshop") => new
    {
        title,
        description = "Cross-connection control",
        chapter = Chapters.Ncr,
        venue = "PICC",
        startsAt = DateTimeOffset.UtcNow.AddDays(10),
        endsAt = DateTimeOffset.UtcNow.AddDays(10).AddHours(4),
        capacity = 100,
        feeOnsite = 500m,
        feeOnline = 200m,
        status = "Published",
    };

    [Fact]
    public async Task CreateEvent_WithoutEventsManage_ReturnsForbidden()
    {
        var memberToken = await _client.RegisterAndLoginAsync();

        var response = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), memberToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_AsAdmin_Succeeds()
    {
        var (_, adminToken) = await CreateAdminAsync();

        var response = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), adminToken));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("cpdUnitsOnsite").ValueKind == JsonValueKind.Null);
        Assert.Equal(1, body.GetProperty("sessions").GetArrayLength());
    }

    [Fact]
    public async Task RecordCashPayment_WithoutEventsManage_ReturnsForbidden()
    {
        var (_, adminToken) = await CreateAdminAsync();
        var createResponse = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), adminToken));
        var eventId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var memberToken = await RegisterMemberAsync();
        var registerResponse = await _client.SendAsync(PostJson($"/api/events/{eventId}/register", new { mode = "Onsite" }, memberToken));
        var registrationId = (await registerResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await _client.SendAsync(
            PostJson($"/api/events/registrations/{registrationId}/payment/cash", new { amount = 500m }, memberToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FullRoundTrip_RegisterThroughCertificate_Succeeds()
    {
        var (adminUserId, adminToken) = await CreateAdminAsync();
        var createResponse = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), adminToken));
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var eventId = created.GetProperty("id").GetGuid();
        var sessionId = created.GetProperty("sessions")[0].GetProperty("id").GetGuid();

        var setUnits = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Put, $"/api/events/{eventId}")
        {
            Content = JsonContent.Create(new
            {
                title = created.GetProperty("title").GetString(),
                description = (string?)null,
                chapter = Chapters.Ncr,
                venue = "PICC",
                startsAt = created.GetProperty("startsAt").GetDateTimeOffset(),
                endsAt = created.GetProperty("endsAt").GetDateTimeOffset(),
                capacity = 100,
                feeOnsite = 500m,
                feeOnline = 200m,
                cpdUnitsOnsite = 8m,
                cpdUnitsOnline = (decimal?)null,
                sessions = new[] { new { id = sessionId, title = "Full Event", startsAt = created.GetProperty("startsAt").GetDateTimeOffset(), endsAt = created.GetProperty("endsAt").GetDateTimeOffset(), order = 1 } },
                status = "Published",
            }),
        }.WithBearer(adminToken));
        Assert.Equal(HttpStatusCode.OK, setUnits.StatusCode);

        var memberToken = await RegisterMemberAsync();
        var registerResponse = await _client.SendAsync(PostJson($"/api/events/{eventId}/register", new { mode = "Onsite" }, memberToken));
        var registrationId = (await registerResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var cashResponse = await _client.SendAsync(
            PostJson($"/api/events/registrations/{registrationId}/payment/cash", new { amount = 500m }, adminToken));
        Assert.Equal(HttpStatusCode.OK, cashResponse.StatusCode);

        var attendanceResponse = await _client.SendAsync(PostJson(
            $"/api/events/{eventId}/roster/attendance",
            new { registrants = new[] { new { registrationId, sessionIds = new[] { sessionId } } } },
            adminToken));
        Assert.Equal(HttpStatusCode.NoContent, attendanceResponse.StatusCode);

        var evaluationResponse = await _client.SendAsync(PostJson(
            $"/api/events/registrations/{registrationId}/evaluation", new { rating = 5, comments = "Great" }, memberToken));
        Assert.Equal(HttpStatusCode.NoContent, evaluationResponse.StatusCode);

        var cpdResponse = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/members/me/cpd").WithBearer(memberToken));
        var cpdBody = await cpdResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(8m, cpdBody.GetProperty("totalCreditUnits").GetDecimal());

        var certificateResponse = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/events/registrations/{registrationId}/certificate").WithBearer(memberToken));
        Assert.Equal(HttpStatusCode.OK, certificateResponse.StatusCode);
        Assert.Equal("application/pdf", certificateResponse.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Browsing (GetAll here) is allowlisted regardless of membership restriction, but Register
    /// itself carries no such attribute - a member lacking portal access can see the event and
    /// reach the register modal (confirmed by the 200 below) but still can't actually register.
    /// This is what EventRegisterModal.tsx's disabled Register button/message mirrors client-side.
    /// </summary>
    [Fact]
    public async Task MemberLackingPortalAccess_CanBrowse_ButRegisterIsStillBlocked()
    {
        var (_, adminToken) = await CreateAdminAsync();
        var createResponse = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), adminToken));
        var eventId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var memberToken = await RegisterMemberLackingPortalAccessAsync();

        var browseResponse = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/events").WithBearer(memberToken));
        Assert.Equal(HttpStatusCode.OK, browseResponse.StatusCode);

        var registerResponse = await _client.SendAsync(PostJson($"/api/events/{eventId}/register", new { mode = "Onsite" }, memberToken));
        Assert.Equal(HttpStatusCode.Forbidden, registerResponse.StatusCode);
        var body = await registerResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("PORTAL_ACCESS_REQUIRED", body!["code"]);
    }

    /// <summary>
    /// Mirrors FullRoundTrip_RegisterThroughCertificate_Succeeds up through evaluation, then flips
    /// the member to Expired before requesting the certificate - GetCertificate must carry
    /// [AllowExpiredMember] the same as MembersController.GetMyCpd, or MembershipAccessMiddleware's
    /// deny-by-default gate would block a member from downloading proof of CPD credit they already
    /// earned just because their membership later lapsed.
    /// </summary>
    [Fact]
    public async Task GetCertificate_ForExpiredMember_StillSucceeds()
    {
        var (_, adminToken) = await CreateAdminAsync();
        var createResponse = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), adminToken));
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var eventId = created.GetProperty("id").GetGuid();
        var sessionId = created.GetProperty("sessions")[0].GetProperty("id").GetGuid();

        var setUnits = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Put, $"/api/events/{eventId}")
        {
            Content = JsonContent.Create(new
            {
                title = created.GetProperty("title").GetString(),
                description = (string?)null,
                chapter = Chapters.Ncr,
                venue = "PICC",
                startsAt = created.GetProperty("startsAt").GetDateTimeOffset(),
                endsAt = created.GetProperty("endsAt").GetDateTimeOffset(),
                capacity = 100,
                feeOnsite = 500m,
                feeOnline = 200m,
                cpdUnitsOnsite = 8m,
                cpdUnitsOnline = (decimal?)null,
                sessions = new[] { new { id = sessionId, title = "Full Event", startsAt = created.GetProperty("startsAt").GetDateTimeOffset(), endsAt = created.GetProperty("endsAt").GetDateTimeOffset(), order = 1 } },
                status = "Published",
            }),
        }.WithBearer(adminToken));
        Assert.Equal(HttpStatusCode.OK, setUnits.StatusCode);

        var memberToken = await RegisterMemberAsync();
        var registerResponse = await _client.SendAsync(PostJson($"/api/events/{eventId}/register", new { mode = "Onsite" }, memberToken));
        var registrationId = (await registerResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var cashResponse = await _client.SendAsync(
            PostJson($"/api/events/registrations/{registrationId}/payment/cash", new { amount = 500m }, adminToken));
        Assert.Equal(HttpStatusCode.OK, cashResponse.StatusCode);

        var attendanceResponse = await _client.SendAsync(PostJson(
            $"/api/events/{eventId}/roster/attendance",
            new { registrants = new[] { new { registrationId, sessionIds = new[] { sessionId } } } },
            adminToken));
        Assert.Equal(HttpStatusCode.NoContent, attendanceResponse.StatusCode);

        var evaluationResponse = await _client.SendAsync(PostJson(
            $"/api/events/registrations/{registrationId}/evaluation", new { rating = 5, comments = "Great" }, memberToken));
        Assert.Equal(HttpStatusCode.NoContent, evaluationResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var member = await db.EventRegistrations
                .Where(r => r.Id == registrationId)
                .Select(r => r.Member)
                .SingleAsync();
            member.Status = MembershipStatus.Expired;
            member.RenewalDueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-100);
            await db.SaveChangesAsync();
        }

        var certificateResponse = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/events/registrations/{registrationId}/certificate").WithBearer(memberToken));

        Assert.Equal(HttpStatusCode.OK, certificateResponse.StatusCode);
        Assert.Equal("application/pdf", certificateResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetCertificate_BeforeEvaluationSubmitted_ReturnsBadRequest()
    {
        var (_, adminToken) = await CreateAdminAsync();
        var createResponse = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), adminToken));
        var eventId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var memberToken = await RegisterMemberAsync();
        var registerResponse = await _client.SendAsync(PostJson($"/api/events/{eventId}/register", new { mode = "Onsite" }, memberToken));
        var registrationId = (await registerResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/events/registrations/{registrationId}/certificate").WithBearer(memberToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadThenGetPoster_RoundTrips()
    {
        var (_, adminToken) = await CreateAdminAsync();
        var createResponse = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), adminToken));
        var eventId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var uploadResponse = await _client.SendAsync(
            UploadTestHelpers.BuildUploadRequest($"/api/events/{eventId}/poster", adminToken, UploadTestHelpers.BuildPng(200, 100), "poster.png", "image/png"));
        Assert.Equal(HttpStatusCode.NoContent, uploadResponse.StatusCode);

        var memberToken = await RegisterMemberAsync();
        var getResponse = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/events/{eventId}/poster").WithBearer(memberToken));

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal("image/jpeg", getResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UploadPoster_NonAdmin_Forbidden()
    {
        var (_, adminToken) = await CreateAdminAsync();
        var createResponse = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), adminToken));
        var eventId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var memberToken = await RegisterMemberAsync();

        var response = await _client.SendAsync(
            UploadTestHelpers.BuildUploadRequest($"/api/events/{eventId}/poster", memberToken, UploadTestHelpers.BuildPng(10, 10), "poster.png", "image/png"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetPoster_BeforeAnyUpload_ReturnsNotFound()
    {
        var (_, adminToken) = await CreateAdminAsync();
        var createResponse = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), adminToken));
        var eventId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var memberToken = await RegisterMemberAsync();

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/events/{eventId}/poster").WithBearer(memberToken));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UploadPoster_NonImageFile_BadRequest()
    {
        var (_, adminToken) = await CreateAdminAsync();
        var createResponse = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), adminToken));
        var eventId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await _client.SendAsync(
            UploadTestHelpers.BuildUploadRequest($"/api/events/{eventId}/poster", adminToken, Encoding.UTF8.GetBytes("fake-pdf"), "poster.pdf", "application/pdf"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadPoster_Oversized_BadRequest()
    {
        var (_, adminToken) = await CreateAdminAsync();
        var createResponse = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), adminToken));
        var eventId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var oversized = new byte[8 * 1024 * 1024 + 1];

        var response = await _client.SendAsync(
            UploadTestHelpers.BuildUploadRequest($"/api/events/{eventId}/poster", adminToken, oversized, "poster.png", "image/png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
