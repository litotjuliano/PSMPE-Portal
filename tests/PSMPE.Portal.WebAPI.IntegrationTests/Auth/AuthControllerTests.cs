using System.Net;
using System.Net.Http.Json;
using PSMPE.Portal.Application.Auth;
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
    public async Task Register_ThenLogin_ReturnsJwtWithContentCreatorRole()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var register = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Password123!", "Test User"));

        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var registerBody = await register.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(registerBody);
        Assert.Contains("Content Creator", registerBody!.Roles);
        Assert.False(string.IsNullOrWhiteSpace(registerBody.Token));

        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Password123!"));

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginBody = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(loginBody);
        Assert.False(string.IsNullOrWhiteSpace(loginBody!.Token));
        Assert.Contains("Content Creator", loginBody.Roles);
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
