using System.Net;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Authorization;

/// <summary>
/// End-to-end check of what the seeded Approval role can and cannot reach: it works the
/// membership-approval queue, verifies/rejects payment proofs (payments:verify) and manages events,
/// but has no member CRUD, fee editing, or user/role administration. Uses real JWTs minted from the
/// real IdentitySeeder grants, so a seed or policy regression shows up here.
///
/// "Allowed" cases send an empty/bogus body and only assert the request got *past* authorization
/// (anything but 401/403) - the 400/404 that follows is the endpoint's own validation, not under test.
/// </summary>
public class ApprovalRolePermissionsTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private string _approvalToken = string.Empty;

    public ApprovalRolePermissionsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (_, _approvalToken) = await _client.CreatePrivilegedUserAsync(userManager, RoleNames.Approval);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url).WithBearer(_approvalToken);
        if (method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return _client.SendAsync(request);
    }

    public static TheoryData<string, string> Allowed => new()
    {
        { "POST", "/api/payments/00000000-0000-0000-0000-000000000001/verify" },
        { "POST", "/api/payments/00000000-0000-0000-0000-000000000001/reject" },
        { "POST", "/api/events" },
        { "PUT", "/api/events/00000000-0000-0000-0000-000000000001" },
        { "GET", "/api/events/00000000-0000-0000-0000-000000000001/roster" },
        { "POST", "/api/members/00000000-0000-0000-0000-000000000001/approve" },
        { "GET", "/api/members" },
        { "GET", "/api/payments" },
    };

    public static TheoryData<string, string> Forbidden => new()
    {
        { "POST", "/api/members" },
        { "PUT", "/api/members/00000000-0000-0000-0000-000000000001" },
        { "DELETE", "/api/members/00000000-0000-0000-0000-000000000001" },
        { "PUT", "/api/payments/fees" },
        { "POST", "/api/payments/fees/promotions" },
        { "GET", "/api/admin/users" },
        { "GET", "/api/admin/users/00000000-0000-0000-0000-000000000001" },
        { "POST", "/api/admin/users" },
        { "POST", "/api/admin/users/00000000-0000-0000-0000-000000000001/password-reset" },
        { "GET", "/api/admin/roles" },
        { "GET", "/api/admin/permissions" },
        { "PUT", "/api/admin/roles/00000000-0000-0000-0000-000000000001/permissions" },
    };

    [Theory]
    [MemberData(nameof(Allowed))]
    public async Task Approval_CanReach(string method, string url)
    {
        var response = await SendAsync(new HttpMethod(method), url);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Forbidden))]
    public async Task Approval_IsForbiddenFrom(string method, string url)
    {
        var response = await SendAsync(new HttpMethod(method), url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
