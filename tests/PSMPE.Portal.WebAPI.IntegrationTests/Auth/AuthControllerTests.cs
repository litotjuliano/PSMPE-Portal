using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using PSMPE.Portal.Application.Auth;
using PSMPE.Portal.Domain.Enums;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Auth;

public class AuthControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Register_ThenLogin_ReturnsJwtWithMemberRole()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var register = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Password123!", "Test User"));

        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var registerBody = await register.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(registerBody);
        Assert.Contains("Member", registerBody!.Roles);
        Assert.False(string.IsNullOrWhiteSpace(registerBody.Token));

        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Password123!"));

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginBody = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(loginBody);
        Assert.False(string.IsNullOrWhiteSpace(loginBody!.Token));
        Assert.Contains("Member", loginBody.Roles);
    }

    [Fact]
    public async Task Register_ReturnsJwtWithSeededMemberPermissionClaims()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var register = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Password123!", "Test User"));

        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var registerBody = await register.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(registerBody);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(registerBody!.Token);
        var permissionClaims = token.Claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToList();

        Assert.Contains(Permissions.Content.Create, permissionClaims);
        Assert.Contains(Permissions.Content.Update, permissionClaims);
        Assert.DoesNotContain(Permissions.Content.Delete, permissionClaims);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "Password123!", "Test User"));

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "WrongPassword!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
