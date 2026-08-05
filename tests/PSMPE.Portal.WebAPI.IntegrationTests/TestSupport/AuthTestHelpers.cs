using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using PSMPE.Portal.Application.Auth;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;

/// <summary>
/// Shared real-HTTP auth flows for integration tests that need a genuine JWT (as opposed to
/// MembersControllerTests.cs's direct controller invocation, which bypasses auth entirely).
/// Extracted from MemberUploadsTests.cs/MemberCertificatesTests.cs, which previously each carried
/// their own copy of the exact same three methods.
/// </summary>
public static class AuthTestHelpers
{
    private static int _clientIpCounter;

    /// <summary>
    /// A client IP no other request in the assembly will reuse. The auth endpoints are rate
    /// limited per client IP (see RateLimitingServiceExtensions), and every test request would
    /// otherwise resolve to FakeRemoteIpStartupFilter's single proxy peer - so a class making
    /// more than the limit's worth of auth calls would start collecting 429s purely by test
    /// count. Each caller is a distinct notional user, so a distinct address is also the honest
    /// simulation; the alternative of loosening the limits would leave them untested.
    /// Sequential rather than random so it cannot collide, and inside 198.18.0.0/15 (the
    /// benchmarking range, RFC 2544) so it is unmistakably synthetic.
    /// </summary>
    public static string NextClientIp()
    {
        var n = Interlocked.Increment(ref _clientIpCounter);
        return $"198.18.{n / 256 % 256}.{n % 256}";
    }

    /// <summary>Posts JSON as if from a brand new client IP, so the request lands in its own
    /// rate limit partition. Drop-in replacement for HttpClient.PostAsJsonAsync.</summary>
    public static Task<HttpResponseMessage> PostAsJsonFromNewClientIpAsync<TValue>(
        this HttpClient client, string requestUri, TValue value)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(value)
        };
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        return client.SendAsync(request);
    }

    /// <summary>Registers, then completes the required email-verification step via the
    /// dev-only verification link (no real email provider exists - see AuthController), so the
    /// resulting token actually works. Always yields a Member-role-only token.</summary>
    public static async Task<string> RegisterAndLoginAsync(this HttpClient client, string displayName = "Test User")
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var register = await client.PostAsJsonFromNewClientIpAsync("/api/auth/register",
            new RegisterRequest(email, "Password123!", displayName, DataPrivacyConsent: true));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();

        var (userId, token) = ParseVerificationLink(registerBody!.DevVerificationLink!);
        var verify = await client.PostAsJsonFromNewClientIpAsync(
            "/api/auth/verify-email", new VerifyEmailRequest(userId, token));
        var verifyBody = await verify.Content.ReadFromJsonAsync<AuthResponse>();
        return verifyBody!.Token;
    }

    public static (Guid UserId, string Token) ParseVerificationLink(string link)
    {
        var uri = new Uri(link);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        return (Guid.Parse(query["userId"]!), query["token"]!);
    }

    /// <summary>Bypasses the public register endpoint (always Member-only) to get a real token
    /// for a privileged role, via a real login so the JWT carries genuine permission claims.
    /// EmailConfirmed is set directly since this shortcut also bypasses the verification flow.</summary>
    public static async Task<(Guid UserId, string Token)> CreatePrivilegedUserAsync(
        this HttpClient client, UserManager<ApplicationUser> userManager, string role)
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = "Privileged Tester", EmailConfirmed = true };
        await userManager.CreateAsync(user, "Password123!");
        await userManager.AddToRoleAsync(user, role);

        var login = await client.PostAsJsonFromNewClientIpAsync(
            "/api/auth/login", new LoginRequest(email, "Password123!"));
        var body = await login.Content.ReadFromJsonAsync<AuthResponse>();
        return (user.Id, body!.Token);
    }

    public static HttpRequestMessage WithBearer(this HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
