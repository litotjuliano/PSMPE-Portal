using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Auth;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using SkiaSharp;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Members;

/// <summary>
/// Exercises the member-scoped upload endpoints (POST/GET /api/members/me/photo, /prc-id, and
/// the admin-viewing /{id}/photo, /prc-id) via real HTTP - unlike MembersControllerTests.cs,
/// these need the actual [Authorize]/[RequirePermission] pipeline (401/403 checks) and real
/// multipart form binding, which direct controller invocation bypasses entirely.
/// </summary>
public class MemberUploadsTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly HttpClient _client;

    public MemberUploadsTests(CustomWebApplicationFactory factory)
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

    private static byte[] BuildPng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Registers, then completes the required email-verification step via the
    /// dev-only verification link (no real email provider exists - see AuthController), so the
    /// resulting token actually works.</summary>
    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var register = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Password123!", "Upload Tester"));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();

        var (userId, token) = ParseVerificationLink(registerBody!.DevVerificationLink!);
        var verify = await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(userId, token));
        var verifyBody = await verify.Content.ReadFromJsonAsync<AuthResponse>();
        return verifyBody!.Token;
    }

    private static (Guid UserId, string Token) ParseVerificationLink(string link)
    {
        var uri = new Uri(link);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        return (Guid.Parse(query["userId"]!), query["token"]!);
    }

    /// <summary>Bypasses the public register endpoint (always Member-only) to get a real token
    /// for a privileged role, via a real login so the JWT carries genuine permission claims.
    /// EmailConfirmed is set directly since this shortcut also bypasses the verification flow.</summary>
    private async Task<(Guid UserId, string Token)> CreatePrivilegedUserAsync(string role)
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = "Privileged Tester", EmailConfirmed = true };
        await _userManager.CreateAsync(user, "Password123!");
        await _userManager.AddToRoleAsync(user, role);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Password123!"));
        var body = await login.Content.ReadFromJsonAsync<AuthResponse>();
        return (user.Id, body!.Token);
    }

    private static HttpRequestMessage BuildUploadRequest(string url, string token, byte[] bytes, string fileName, string contentType)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        request.Content = content;
        return request;
    }

    [Fact]
    public async Task UploadThenGetMyPhoto_RoundTrips()
    {
        var token = await RegisterAndLoginAsync();

        var uploadResponse = await _client.SendAsync(BuildUploadRequest("/api/members/me/photo", token, BuildPng(100, 100), "photo.png", "image/png"));
        Assert.Equal(HttpStatusCode.NoContent, uploadResponse.StatusCode);

        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/api/members/me/photo");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var getResponse = await _client.SendAsync(getRequest);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal("image/jpeg", getResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetMyPhoto_BeforeAnyUpload_ReturnsNotFound()
    {
        var token = await RegisterAndLoginAsync();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/api/members/me/photo");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(getRequest);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReUpload_ReplacesThePreviousFile()
    {
        var token = await RegisterAndLoginAsync();

        await _client.SendAsync(BuildUploadRequest("/api/members/me/photo", token, BuildPng(50, 50), "first.png", "image/png"));
        await _client.SendAsync(BuildUploadRequest("/api/members/me/photo", token, BuildPng(300, 200), "second.png", "image/png"));

        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/api/members/me/photo");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(getRequest);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var result = SKBitmap.Decode(bytes);

        Assert.Equal(300, result!.Width);
        Assert.Equal(200, result.Height);
    }

    [Fact]
    public async Task UploadLargeDimensionImage_IsResizedToFitWithinMaxDimension()
    {
        var token = await RegisterAndLoginAsync();

        await _client.SendAsync(BuildUploadRequest("/api/members/me/photo", token, BuildPng(2400, 1800), "big.png", "image/png"));

        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/api/members/me/photo");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(getRequest);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var result = SKBitmap.Decode(bytes);

        Assert.True(Math.Max(result!.Width, result.Height) <= 1600);
    }

    [Fact]
    public async Task UploadPdfToPhotoEndpoint_ReturnsBadRequest()
    {
        var token = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(
            BuildUploadRequest("/api/members/me/photo", token, Encoding.UTF8.GetBytes("fake-pdf"), "doc.pdf", "application/pdf"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadPdfToPrcIdEndpoint_Succeeds()
    {
        var token = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(
            BuildUploadRequest("/api/members/me/prc-id", token, Encoding.UTF8.GetBytes("fake-pdf-bytes"), "id.pdf", "application/pdf"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task UploadOversizedPdf_ReturnsBadRequest()
    {
        var token = await RegisterAndLoginAsync();
        var oversized = new byte[3 * 1024 * 1024];

        var response = await _client.SendAsync(
            BuildUploadRequest("/api/members/me/prc-id", token, oversized, "big.pdf", "application/pdf"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_WithoutAuth_ReturnsUnauthorized()
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(BuildPng(10, 10));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "photo.png");

        var response = await _client.PostAsync("/api/members/me/photo", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanViewAnotherMembersPhoto()
    {
        var memberToken = await RegisterAndLoginAsync();
        await _client.SendAsync(BuildUploadRequest("/api/members/me/photo", memberToken, BuildPng(80, 80), "photo.png", "image/png"));

        var profileRequest = new HttpRequestMessage(HttpMethod.Put, "/api/members/me");
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        profileRequest.Content = JsonContent.Create(new
        {
            firstName = "View",
            middleName = (string?)null,
            lastName = "Target",
            suffix = (string?)null,
            birthdate = (string?)null,
            gender = (string?)null,
            address = (string?)null,
            prcLicenseNo = (string?)null,
            chapter = Chapters.Ncr,
            company = (string?)null,
            memberType = MemberTypes.Regular
        });
        var profileResponse = await _client.SendAsync(profileRequest);
        var profileBody = await profileResponse.Content.ReadFromJsonAsync<JsonElement>();
        var memberId = profileBody.GetProperty("id").GetGuid();

        var (_, adminToken) = await CreatePrivilegedUserAsync(RoleNames.Admin);
        var viewRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/members/{memberId}/photo");
        viewRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var viewResponse = await _client.SendAsync(viewRequest);

        Assert.Equal(HttpStatusCode.OK, viewResponse.StatusCode);
    }

    [Fact]
    public async Task PlainMember_CannotViewAnotherMembersPhoto()
    {
        var memberToken = await RegisterAndLoginAsync();
        await _client.SendAsync(BuildUploadRequest("/api/members/me/photo", memberToken, BuildPng(80, 80), "photo.png", "image/png"));

        var profileRequest = new HttpRequestMessage(HttpMethod.Put, "/api/members/me");
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        profileRequest.Content = JsonContent.Create(new
        {
            firstName = "View",
            middleName = (string?)null,
            lastName = "Target2",
            suffix = (string?)null,
            birthdate = (string?)null,
            gender = (string?)null,
            address = (string?)null,
            prcLicenseNo = (string?)null,
            chapter = Chapters.Ncr,
            company = (string?)null,
            memberType = MemberTypes.Regular
        });
        var profileResponse = await _client.SendAsync(profileRequest);
        var profileBody = await profileResponse.Content.ReadFromJsonAsync<JsonElement>();
        var memberId = profileBody.GetProperty("id").GetGuid();

        var otherToken = await RegisterAndLoginAsync();
        var viewRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/members/{memberId}/photo");
        viewRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
        var viewResponse = await _client.SendAsync(viewRequest);

        Assert.Equal(HttpStatusCode.Forbidden, viewResponse.StatusCode);
    }
}
