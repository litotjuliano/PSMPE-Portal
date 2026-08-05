using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using PSMPE.Portal.Application.Auth;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Auth;

public class AccountLockoutTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AccountLockoutTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static string UniqueIp() => $"198.51.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 255)}";

    /// <summary>Registers and verifies, so the account is loginable and lockout is what fails it.</summary>
    private async Task<string> VerifiedAccountAsync()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var register = await SendAsync(HttpMethod.Post, "/api/auth/register",
            new RegisterRequest(email, "Password123!", "Lockout Tester", DataPrivacyConsent: true), UniqueIp());
        var body = await register.Content.ReadFromJsonAsync<RegisterResponse>();

        var uri = new Uri(body!.DevVerificationLink!);
        var query = QueryHelpers.ParseQuery(uri.Query);
        await SendAsync(HttpMethod.Post, "/api/auth/verify-email",
            new VerifyEmailRequest(Guid.Parse(query["userId"]!), query["token"]!), UniqueIp());

        return email;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object payload, string clientIp)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-Forwarded-For", clientIp);
        return await _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password, string? clientIp = null) =>
        SendAsync(HttpMethod.Post, "/api/auth/login", new LoginRequest(email, password), clientIp ?? UniqueIp());

    [Fact]
    public async Task Login_AfterFiveFailures_LocksTheAccount()
    {
        var email = await VerifiedAccountAsync();
        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(email, "WrongPassword1!");
        }

        var response = await LoginAsync(email, "WrongPassword1!");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("ACCOUNT_LOCKED", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_LockoutHoldsAcrossDifferentClientIps()
    {
        var email = await VerifiedAccountAsync();

        // The whole point of lockout: rotating IPs defeats the per-IP limiter but not this.
        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(email, "WrongPassword1!", UniqueIp());
        }

        var response = await LoginAsync(email, "WrongPassword1!", UniqueIp());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("ACCOUNT_LOCKED", body.GetProperty("code").GetString());
    }

    /// <summary>
    /// The property that makes lockout worth having: while locked, the account is closed to
    /// everyone, including whoever finally guesses right. Without this, the lockout check could
    /// be moved after the password check and every other test here would still pass, leaving a
    /// locked account perfectly usable by an attacker who lands the password on the next try.
    /// </summary>
    [Fact]
    public async Task Login_WhileLockedOut_RefusesEvenTheCorrectPassword()
    {
        var email = await VerifiedAccountAsync();
        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(email, "WrongPassword1!");
        }

        var response = await LoginAsync(email, "Password123!");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("ACCOUNT_LOCKED", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_SuccessfulAttempt_ResetsTheFailureCount()
    {
        var email = await VerifiedAccountAsync();
        for (var i = 0; i < 4; i++)
        {
            await LoginAsync(email, "WrongPassword1!");
        }

        var good = await LoginAsync(email, "Password123!");
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);

        // If the count had survived, four more failures would lock the account.
        for (var i = 0; i < 4; i++)
        {
            var response = await LoginAsync(email, "WrongPassword1!");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Login_AgainstUnknownEmail_StaysGenericAndDoesNotLeakExistence()
    {
        var unknown = $"{Guid.NewGuid()}@example.com";
        for (var i = 0; i < 6; i++)
        {
            var response = await LoginAsync(unknown, "WrongPassword1!");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task ResetPassword_ClearsTheLockout_SoTheMemberCanActuallySignIn()
    {
        // The path a real member takes: forget the password, fail five times, then reset it.
        // Without lockout being cleared here the reset appears to work and login still refuses,
        // which reads as "the reset didn't take" and sends them back for another reset email -
        // straight into the 3-per-hour cap on those.
        var email = await VerifiedAccountAsync();
        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(email, "WrongPassword1!");
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await LoginAsync(email, "WrongPassword1!")).StatusCode);

        var forgot = await SendAsync(HttpMethod.Post, "/api/auth/forgot-password",
            new ForgotPasswordRequest(email), UniqueIp());
        var forgotBody = await forgot.Content.ReadFromJsonAsync<ForgotPasswordResponse>();

        var query = QueryHelpers.ParseQuery(new Uri(forgotBody!.DevResetLink!).Query);
        var reset = await SendAsync(HttpMethod.Post, "/api/auth/reset-password",
            new ResetPasswordRequest(Guid.Parse(query["userId"]!), query["token"]!, "NewPassword123!"),
            UniqueIp());
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var login = await LoginAsync(email, "NewPassword123!");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
}
