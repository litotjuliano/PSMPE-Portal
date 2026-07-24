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
        yield return [HttpMethod.Put, $"/api/members/{id}", new
        {
            firstName = "X", middleName = (string?)null, lastName = "Y", suffix = (string?)null,
            birthdate = (string?)null, gender = (string?)null, civilStatus = (string?)null, address = (string?)null,
            mobileNumber = (string?)null, housePhone = (string?)null, website = (string?)null, facebookUrl = (string?)null,
            linkedInUrl = (string?)null, xUrl = (string?)null, instagramUrl = (string?)null,
            prcLicenseNo = (string?)null, ptrNumber = (string?)null, tin = (string?)null,
            chapter = Chapters.Ncr, employmentStatus = (string?)null, company = (string?)null, position = (string?)null,
            businessAddress = (string?)null, yearsOfPractice = (int?)null, specialization = (string?)null, skills = (string?)null,
            memberType = MemberTypes.Regular, status = 0, renewalDueDate = (string?)null, nationalDuesReferenceNo = (string?)null
        }];
        yield return [HttpMethod.Post, $"/api/members/{id}/approve", null!];
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
}
