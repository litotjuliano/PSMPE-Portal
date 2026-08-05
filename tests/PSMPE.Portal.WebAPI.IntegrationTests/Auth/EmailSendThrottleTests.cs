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

    private async Task<ForgotPasswordResponse> ForgotPasswordAsync(string email)
    {
        var response = await SendAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ForgotPasswordResponse>())!;
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
}
