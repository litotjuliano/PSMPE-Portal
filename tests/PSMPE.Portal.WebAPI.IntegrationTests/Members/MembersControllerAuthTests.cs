using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Members;

/// <summary>
/// Exercises the [Authorize]/[RequirePermission] boundary on MembersController's admin-only
/// endpoints via real HTTP - unlike MembersControllerTests.cs (which instantiates the controller
/// directly and so bypasses the auth pipeline entirely), these prove that a missing/insufficient
/// token actually gets rejected, not just that the business logic behaves once past auth.
/// </summary>
public class MembersControllerAuthTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly HttpClient _client;

    public MembersControllerAuthTests(CustomWebApplicationFactory factory)
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

    private static HttpRequestMessage Request(HttpMethod method, string url, string? token = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return request;
    }

    public static IEnumerable<object[]> AdminOnlyEndpoints()
    {
        var id = Guid.NewGuid();
        yield return [HttpMethod.Get, "/api/members", null!];
        yield return [HttpMethod.Get, "/api/members/stats", null!];
        yield return [HttpMethod.Put, $"/api/members/{id}", new
        {
            firstName = "X", middleName = (string?)null, lastName = "Y", suffix = (string?)null,
            birthdate = (string?)null, gender = (string?)null, civilStatus = (string?)null, address = (string?)null,
            mobileNumber = (string?)null, housePhone = (string?)null,
            prcLicenseNo = (string?)null, ptrNumber = (string?)null, tin = (string?)null,
            chapter = Chapters.Ncr, employmentStatus = (string?)null, company = (string?)null, position = (string?)null,
            businessAddress = (string?)null, yearsOfPractice = (int?)null, specialization = (string?)null, skills = (string?)null,
            memberType = MemberTypes.Regular, status = 0, renewalDueDate = (string?)null, nationalDuesReferenceNo = (string?)null
        }];
        yield return [HttpMethod.Post, $"/api/members/{id}/approve", null!];
        // Reports whether a control number is taken - not something an unprivileged caller should
        // be able to probe, so it carries the same permission as approving.
        yield return [HttpMethod.Get, "/api/members/membership-no/availability?value=PSMPE-1", null!];
        yield return [HttpMethod.Post, $"/api/members/{id}/prc-verification/approve", null!];
        yield return [HttpMethod.Post, $"/api/members/{id}/prc-verification/reject", new { reason = "Illegible document" }];
    }

    [Theory]
    [MemberData(nameof(AdminOnlyEndpoints))]
    public async Task AdminOnlyEndpoint_WithoutAuth_ReturnsUnauthorized(HttpMethod method, string url, object? body)
    {
        var response = await _client.SendAsync(Request(method, url, token: null, body));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminOnlyEndpoints))]
    public async Task AdminOnlyEndpoint_AsPlainMember_ReturnsForbidden(HttpMethod method, string url, object? body)
    {
        var token = await _client.RegisterAndLoginAsync("Auth Boundary Tester");

        var response = await _client.SendAsync(Request(method, url, token, body));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_AsAdmin_ReturnsOk()
    {
        var (_, adminToken) = await _client.CreatePrivilegedUserAsync(_userManager, RoleNames.Admin);

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/members", adminToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetStats_AsAdmin_ReturnsOk()
    {
        var (_, adminToken) = await _client.CreatePrivilegedUserAsync(_userManager, RoleNames.Admin);

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/members/stats", adminToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendPasswordReset_AsPlainMember_IsRefused()
    {
        // Gated on admin:manage-users, which a Member does not hold.
        var memberToken = await _client.RegisterAndLoginAsync("Reset Boundary Tester");
        var target = Guid.NewGuid();

        var response = await _client.SendAsync(
            Request(HttpMethod.Post, $"/api/admin/users/{target}/password-reset", memberToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMyProfile_AsAdministrativeAccount_ExplainsWhyRatherThanReturningAnEmptyBody()
    {
        // Administrative accounts have no Member row by design, so this is a deliberate refusal.
        // It previously used Forbid(), which writes a ZERO-BYTE body - the frontend had nothing to
        // display and the user saw a silent failure on /profile. Observed live on staging as
        // repeated 403s with 0 bytes. The code is what lets the client explain it instead.
        var (_, adminToken) = await _client.CreatePrivilegedUserAsync(_userManager, RoleNames.Admin);
        var body = new
        {
            firstName = "Admin", lastName = "Account",
            chapter = Chapters.Ncr, memberType = MemberTypes.Regular,
        };

        var response = await _client.SendAsync(Request(HttpMethod.Put, "/api/members/me", adminToken, body));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("ADMIN_ACCOUNT_NO_PROFILE", payload.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task UploadMyPhoto_AsAdministrativeAccount_IsAllowed()
    {
        // The counterpart to the refusal above, and the reason it must stay narrow: uploads are
        // keyed by UserId rather than MemberId, so an administrator can set an account photo even
        // with no membership profile. If this ever starts failing, /profile has nothing to offer
        // an administrative account at all.
        var (_, adminToken) = await _client.CreatePrivilegedUserAsync(_userManager, RoleNames.Admin);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/members/me/photo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(file, "file", "photo.png");
        request.Content = content;

        var response = await _client.SendAsync(request);

        Assert.True(
            response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK,
            $"expected the upload to succeed, got {(int)response.StatusCode} {response.StatusCode}");
    }
}
