using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
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
    /// approved member" the same way MembershipAccessMiddlewareTests does.
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
        fee = 500m,
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
                fee = 500m,
                cpdUnitsOnsite = 8m,
                cpdUnitsOnline = (decimal?)null,
                sessions = new[] { new { id = sessionId, title = "Full Event", startsAt = created.GetProperty("startsAt").GetDateTimeOffset(), endsAt = created.GetProperty("endsAt").GetDateTimeOffset(), order = 1 } },
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
}
