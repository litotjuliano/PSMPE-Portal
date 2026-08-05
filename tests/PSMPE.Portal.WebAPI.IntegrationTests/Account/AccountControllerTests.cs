using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Account;
using PSMPE.Portal.Application.Auth;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Account;

/// <summary>
/// Self-service account management over real HTTP. Before this existed no account of any role
/// could change its own display name or password, and the Super Admin account could not be edited
/// at all - so these cover both a plain member and a privileged caller.
/// </summary>
public class AccountControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly HttpClient _client;

    public AccountControllerTests(CustomWebApplicationFactory factory)
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

    private HttpRequestMessage Request(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        // Distinct client IP per request so the rate limiter's auth-ip partition can't make these
        // tests fail by run order - see AuthTestHelpers.NextClientIp.
        request.Headers.Add("X-Forwarded-For", AuthTestHelpers.NextClientIp());
        return request;
    }

    [Fact]
    public async Task UpdateMyAccount_ChangesDisplayName_AndReturnsTheNewState()
    {
        var token = await _client.RegisterAndLoginAsync("Original Name");

        var response = await _client.SendAsync(
            Request(HttpMethod.Put, "/api/account/me", token, new UpdateAccountRequest("Renamed Person")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AccountDto>();
        // Returned rather than requiring a re-read: the client caches the user at sign-in and would
        // otherwise show the old name until the token expired, which reads as a failed save.
        Assert.Equal("Renamed Person", body!.DisplayName);
        Assert.Contains(RoleNames.Member, body.Roles);
        Assert.False(string.IsNullOrWhiteSpace(body.Email));
    }

    [Fact]
    public async Task UpdateMyAccount_AsAdministrativeAccount_IsAllowed()
    {
        // The gap this change closes: privileged accounts had no self-service path at all.
        var (_, adminToken) = await _client.CreatePrivilegedUserAsync(_userManager, RoleNames.Admin);

        var response = await _client.SendAsync(
            Request(HttpMethod.Put, "/api/account/me", adminToken, new UpdateAccountRequest("Staff Member")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AccountDto>();
        Assert.Equal("Staff Member", body!.DisplayName);
    }

    [Fact]
    public async Task UpdateMyAccount_WithoutAToken_IsRefused()
    {
        var response = await _client.PutAsJsonAsync("/api/account/me", new UpdateAccountRequest("Nobody"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangeMyPassword_WithTheCorrectCurrentPassword_SwapsTheCredentials()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = "Password Changer", EmailConfirmed = true };
        await _userManager.CreateAsync(user, "Password123!");
        await _userManager.AddToRoleAsync(user, RoleNames.Member);
        var token = await LoginAsync(email, "Password123!");

        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/account/me/password", token,
            new ChangePasswordRequest("Password123!", "BrandNewPass456!")));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginResponseAsync(email, "BrandNewPass456!")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginResponseAsync(email, "Password123!")).StatusCode);
    }

    [Fact]
    public async Task ChangeMyPassword_WithTheWrongCurrentPassword_IsRefusedWithoutSayingWhichHalfFailed()
    {
        var token = await _client.RegisterAndLoginAsync("Wrong Current");

        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/account/me/password", token,
            new ChangePasswordRequest("NotMyPassword1!", "BrandNewPass456!")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // Must not disclose that the NEW password was fine - that tells a caller holding a stolen
        // token their guess was the only thing wrong, which is worth knowing to an attacker.
        Assert.DoesNotContain("NewPassword", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangeMyPassword_RepeatedWrongAttempts_DoNotLockTheAccountOut()
    {
        // Deliberate: this endpoint already requires a valid token, so it is not an anonymous
        // guessing surface - and counting failures would let anyone holding a stale token lock the
        // real owner out of their own account.
        var email = $"{Guid.NewGuid()}@example.com";
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = "Not Lockable", EmailConfirmed = true };
        await _userManager.CreateAsync(user, "Password123!");
        await _userManager.AddToRoleAsync(user, RoleNames.Member);
        var token = await LoginAsync(email, "Password123!");

        for (var i = 0; i < 8; i++)
        {
            await _client.SendAsync(Request(HttpMethod.Post, "/api/account/me/password", token,
                new ChangePasswordRequest("WrongPassword1!", "BrandNewPass456!")));
        }

        Assert.Equal(HttpStatusCode.OK, (await LoginResponseAsync(email, "Password123!")).StatusCode);
    }

    [Fact]
    public async Task ChangeMyPassword_ClearsAnExistingLockout()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = "Locked Out", EmailConfirmed = true };
        await _userManager.CreateAsync(user, "Password123!");
        await _userManager.AddToRoleAsync(user, RoleNames.Member);
        var token = await LoginAsync(email, "Password123!");

        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1));
        Assert.Equal(HttpStatusCode.Forbidden, (await LoginResponseAsync(email, "Password123!")).StatusCode);

        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/account/me/password", token,
            new ChangePasswordRequest("Password123!", "BrandNewPass456!")));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginResponseAsync(email, "BrandNewPass456!")).StatusCode);
    }

    private async Task<HttpResponseMessage> LoginResponseAsync(string email, string password)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password)),
        };
        request.Headers.Add("X-Forwarded-For", AuthTestHelpers.NextClientIp());
        return await _client.SendAsync(request);
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await LoginResponseAsync(email, password);
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        return body!.Token;
    }
}
