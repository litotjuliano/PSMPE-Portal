using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
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

    private static (Guid UserId, string Token) ParseVerificationLink(string link)
    {
        var uri = new Uri(link);
        var query = QueryHelpers.ParseQuery(uri.Query);
        return (Guid.Parse(query["userId"]!), query["token"]!);
    }

    private async Task<(string Email, Guid UserId, string Token)> RegisterAsync(string? username = null)
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var register = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Password123!", "Test User", username));
        var body = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var (userId, token) = ParseVerificationLink(body!.DevVerificationLink!);
        return (email, userId, token);
    }

    [Fact]
    public async Task Register_ReturnsNoTokenAndADevVerificationLink()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Password123!", "Test User"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(body);
        Assert.Equal(email, body!.Email);
        Assert.NotNull(body.DevVerificationLink);
    }

    [Fact]
    public async Task Login_BeforeVerifying_ReturnsForbiddenWithEmailNotConfirmedCode()
    {
        var (email, _, _) = await RegisterAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Password123!"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("EMAIL_NOT_CONFIRMED", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task VerifyEmail_WithValidToken_ConfirmsAndReturnsWorkingAuthResponse()
    {
        var (email, userId, token) = await RegisterAsync();

        var verify = await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(userId, token));

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var verifyBody = await verify.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(verifyBody);
        Assert.False(string.IsNullOrWhiteSpace(verifyBody!.Token));
        Assert.Contains("Member", verifyBody.Roles);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Password123!"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task VerifyEmail_WithTamperedToken_ReturnsBadRequest()
    {
        var (_, userId, _) = await RegisterAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(userId, "not-a-real-token"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResendVerificationEmail_ForExistingUnverifiedAccount_ReturnsWorkingLink()
    {
        var (email, _, _) = await RegisterAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/resend-verification-email", new ResendVerificationEmailRequest(email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ResendVerificationEmailResponse>();
        Assert.NotNull(body!.DevVerificationLink);

        var (userId, token) = ParseVerificationLink(body.DevVerificationLink!);
        var verify = await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(userId, token));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
    }

    [Fact]
    public async Task ResendVerificationEmail_ForNonexistentEmail_StillReturnsGenericOk()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/resend-verification-email",
            new ResendVerificationEmailRequest($"{Guid.NewGuid()}@example.com"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ResendVerificationEmailResponse>();
        Assert.Null(body!.DevVerificationLink);
    }

    [Fact]
    public async Task Register_ThenVerify_ReturnsJwtWithMemberRole()
    {
        var (_, userId, token) = await RegisterAsync();

        var verify = await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(userId, token));
        var verifyBody = await verify.Content.ReadFromJsonAsync<AuthResponse>();

        Assert.Contains("Member", verifyBody!.Roles);
        Assert.False(string.IsNullOrWhiteSpace(verifyBody.Token));
    }

    [Fact]
    public async Task VerifyEmail_ReturnsJwtWithSeededMemberPermissionClaims()
    {
        var (_, userId, token) = await RegisterAsync();

        var verify = await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(userId, token));
        var verifyBody = await verify.Content.ReadFromJsonAsync<AuthResponse>();

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(verifyBody!.Token);
        var permissionClaims = jwt.Claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToList();

        Assert.Contains(Permissions.Content.Create, permissionClaims);
        Assert.Contains(Permissions.Content.Update, permissionClaims);
        Assert.DoesNotContain(Permissions.Content.Delete, permissionClaims);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var (email, userId, token) = await RegisterAsync();
        await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(userId, token));

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "WrongPassword!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithDistinctUsername_PersistsIt()
    {
        var username = $"user{Guid.NewGuid():N}"[..15];
        var (email, userId, token) = await RegisterAsync(username);
        await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(userId, token));

        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Password123!"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var available = await _client.GetAsync($"/api/auth/username-available?username={username}");
        var isAvailable = await available.Content.ReadFromJsonAsync<bool>();
        Assert.False(isAvailable);
    }

    [Fact]
    public async Task Register_WithCollidingUsername_ReturnsConflict()
    {
        var username = $"user{Guid.NewGuid():N}"[..15];
        await RegisterAsync(username);

        var secondEmail = $"{Guid.NewGuid()}@example.com";
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(secondEmail, "Password123!", "Test User", username));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithoutUsername_PreservesExistingBehavior()
    {
        var (email, userId, token) = await RegisterAsync();
        await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(userId, token));

        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Password123!"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_ForNonexistentEmail_StillReturnsGenericOk()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/forgot-password",
            new ForgotPasswordRequest($"{Guid.NewGuid()}@example.com"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ForgotPasswordResponse>();
        Assert.Null(body!.DevResetLink);
    }

    [Fact]
    public async Task ForgotPassword_ForUnverifiedAccount_StillReturnsGenericOkWithNoLink()
    {
        var (email, _, _) = await RegisterAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ForgotPasswordResponse>();
        Assert.Null(body!.DevResetLink);
    }

    [Fact]
    public async Task ForgotPassword_ThenResetPassword_AllowsLoginWithNewPassword()
    {
        var (email, userId, token) = await RegisterAsync();
        await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(userId, token));

        var forgot = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
        var forgotBody = await forgot.Content.ReadFromJsonAsync<ForgotPasswordResponse>();
        Assert.NotNull(forgotBody!.DevResetLink);

        var (resetUserId, resetToken) = ParseVerificationLink(forgotBody.DevResetLink!);
        var reset = await _client.PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest(resetUserId, resetToken, "NewPassword456!"));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var loginWithNewPassword = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "NewPassword456!"));
        Assert.Equal(HttpStatusCode.OK, loginWithNewPassword.StatusCode);

        var loginWithOldPassword = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Password123!"));
        Assert.Equal(HttpStatusCode.Unauthorized, loginWithOldPassword.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WithTamperedToken_ReturnsBadRequest()
    {
        var (_, userId, token) = await RegisterAsync();
        await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(userId, token));

        var response = await _client.PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest(userId, "not-a-real-token", "NewPassword456!"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WithWeakPassword_ReturnsValidationProblem()
    {
        var (email, userId, token) = await RegisterAsync();
        await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(userId, token));

        var forgot = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        var forgotBody = await forgot.Content.ReadFromJsonAsync<ForgotPasswordResponse>();
        var (resetUserId, resetToken) = ParseVerificationLink(forgotBody!.DevResetLink!);

        var response = await _client.PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest(resetUserId, resetToken, "weak"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(problem.TryGetProperty("errors", out _));
    }
}
