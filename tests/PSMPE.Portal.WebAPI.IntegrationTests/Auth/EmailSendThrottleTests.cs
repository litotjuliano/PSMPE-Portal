using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using PSMPE.Portal.Application.Auth;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Auth;

public class EmailSendThrottleTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public EmailSendThrottleTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static string UniqueIp() => $"198.51.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 255)}";

    private async Task<HttpResponseMessage> SendAsync(string path, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-Forwarded-For", UniqueIp());
        return await _client.SendAsync(request);
    }

    private async Task<string> VerifiedAccountAsync()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var register = await SendAsync("/api/auth/register",
            new RegisterRequest(email, "Password123!", "Throttle Tester", DataPrivacyConsent: true));
        var body = await register.Content.ReadFromJsonAsync<RegisterResponse>();

        var query = QueryHelpers.ParseQuery(new Uri(body!.DevVerificationLink!).Query);
        await SendAsync("/api/auth/verify-email",
            new VerifyEmailRequest(Guid.Parse(query["userId"]!), query["token"]!));

        return email;
    }

    /// <summary>
    /// Registers without verifying - resend-verification-email only sends for an account that
    /// exists and is still unverified, so a verified fixture would be suppressed for the wrong
    /// reason and the test would pass without the throttle doing anything.
    /// </summary>
    private async Task<string> UnverifiedAccountAsync()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        await SendAsync("/api/auth/register",
            new RegisterRequest(email, "Password123!", "Throttle Tester", DataPrivacyConsent: true));
        return email;
    }

    private async Task<ForgotPasswordResponse> ForgotPasswordAsync(string email)
    {
        var response = await SendAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ForgotPasswordResponse>())!;
    }

    private async Task<ResendVerificationEmailResponse> ResendVerificationAsync(string email)
    {
        var response = await SendAsync("/api/auth/resend-verification-email",
            new ResendVerificationEmailRequest(email));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResendVerificationEmailResponse>())!;
    }

    [Fact]
    public async Task ForgotPassword_FourthSendForOneAddress_IsSuppressed()
    {
        var email = await VerifiedAccountAsync();

        for (var i = 0; i < 3; i++)
        {
            Assert.NotNull((await ForgotPasswordAsync(email)).DevResetLink);
        }

        Assert.Null((await ForgotPasswordAsync(email)).DevResetLink);
    }

    [Fact]
    public async Task ForgotPassword_ThrottlingIsPerAddressNotGlobal()
    {
        var exhausted = await VerifiedAccountAsync();
        for (var i = 0; i < 4; i++)
        {
            await ForgotPasswordAsync(exhausted);
        }

        var other = await VerifiedAccountAsync();
        Assert.NotNull((await ForgotPasswordAsync(other)).DevResetLink);
    }

    [Fact]
    public async Task ForgotPassword_ThrottledResponse_IsIndistinguishableFromAnUnthrottledOne()
    {
        var email = await VerifiedAccountAsync();
        var first = await ForgotPasswordAsync(email);
        for (var i = 0; i < 3; i++)
        {
            await ForgotPasswordAsync(email);
        }

        var throttled = await ForgotPasswordAsync(email);

        // Same message and status as a served request - the throttle must not become the
        // enumeration oracle the endpoint otherwise avoids being.
        Assert.Equal(first.Message, throttled.Message);
    }

    [Fact]
    public async Task ResendVerification_FourthSendForOneAddress_IsSuppressed()
    {
        var email = await UnverifiedAccountAsync();

        for (var i = 0; i < 3; i++)
        {
            Assert.NotNull((await ResendVerificationAsync(email)).DevVerificationLink);
        }

        Assert.Null((await ResendVerificationAsync(email)).DevVerificationLink);
    }

    [Fact]
    public async Task ResendVerification_ThrottledResponse_IsIndistinguishableFromAnUnthrottledOne()
    {
        var email = await UnverifiedAccountAsync();
        var first = await ResendVerificationAsync(email);
        for (var i = 0; i < 3; i++)
        {
            await ResendVerificationAsync(email);
        }

        var throttled = await ResendVerificationAsync(email);

        Assert.Equal(first.Message, throttled.Message);
    }

    [Fact]
    public async Task ForgotPasswordAndResendVerification_ShareOneAllowancePerAddress()
    {
        // Both endpoints mail the same inbox, so a per-endpoint allowance would hand an attacker
        // double the sends against it. The two can only ever draw on that allowance in sequence -
        // resend needs an unverified account, forgot-password a verified one - so the account has
        // to cross from one state to the other mid-test for the sharing to be observable at all.
        var email = await UnverifiedAccountAsync();

        string? verificationLink = null;
        for (var i = 0; i < 3; i++)
        {
            verificationLink = (await ResendVerificationAsync(email)).DevVerificationLink;
            Assert.NotNull(verificationLink);
        }

        var query = QueryHelpers.ParseQuery(new Uri(verificationLink!).Query);
        var verify = await SendAsync("/api/auth/verify-email",
            new VerifyEmailRequest(Guid.Parse(query["userId"]!), query["token"]!));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        // The address has spent all three sends on verification emails; a reset email is a fourth.
        Assert.Null((await ForgotPasswordAsync(email)).DevResetLink);
    }
}
