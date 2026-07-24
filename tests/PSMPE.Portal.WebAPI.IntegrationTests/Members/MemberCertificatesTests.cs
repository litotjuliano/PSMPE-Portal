using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Members;

/// <summary>
/// Exercises the member-scoped certificate endpoints (POST/GET/DELETE /api/members/me/certificates)
/// via real HTTP, mirroring MemberUploadsTests.cs's conventions - unlike MemberUpload's single-file-
/// per-kind model, certificates are unbounded per member, so these tests also cover multi-item
/// list/delete behavior that MemberUploadsTests.cs doesn't need.
/// </summary>
public class MemberCertificatesTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly HttpClient _client;

    public MemberCertificatesTests(CustomWebApplicationFactory factory)
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

    private Task<string> RegisterAndLoginAsync() => _client.RegisterAndLoginAsync("Certificate Tester");

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

    private HttpRequestMessage BuildAuthedGet(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task UploadThenListMyCertificates_RoundTrips()
    {
        var token = await RegisterAndLoginAsync();

        var uploadResponse = await _client.SendAsync(
            BuildUploadRequest("/api/members/me/certificates", token, Encoding.UTF8.GetBytes("fake-pdf-bytes"), "cert.pdf", "application/pdf"));
        Assert.Equal(HttpStatusCode.NoContent, uploadResponse.StatusCode);

        var listResponse = await _client.SendAsync(BuildAuthedGet("/api/members/me/certificates", token));
        var certificates = await listResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(1, certificates.GetArrayLength());
        Assert.Equal("cert.pdf", certificates[0].GetProperty("fileName").GetString());
    }

    [Fact]
    public async Task GetMyCertificates_BeforeAnyUpload_ReturnsEmptyList()
    {
        var token = await RegisterAndLoginAsync();

        var listResponse = await _client.SendAsync(BuildAuthedGet("/api/members/me/certificates", token));
        var certificates = await listResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(0, certificates.GetArrayLength());
    }

    [Fact]
    public async Task UploadMultipleCertificates_AllAppearInList()
    {
        var token = await RegisterAndLoginAsync();

        await _client.SendAsync(BuildUploadRequest("/api/members/me/certificates", token, Encoding.UTF8.GetBytes("first"), "first.pdf", "application/pdf"));
        await _client.SendAsync(BuildUploadRequest("/api/members/me/certificates", token, Encoding.UTF8.GetBytes("second"), "second.pdf", "application/pdf"));

        var listResponse = await _client.SendAsync(BuildAuthedGet("/api/members/me/certificates", token));
        var certificates = await listResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, certificates.GetArrayLength());
    }

    [Fact]
    public async Task DeleteMyCertificate_RemovesItFromList()
    {
        var token = await RegisterAndLoginAsync();
        await _client.SendAsync(BuildUploadRequest("/api/members/me/certificates", token, Encoding.UTF8.GetBytes("bytes"), "cert.pdf", "application/pdf"));
        var listResponse = await _client.SendAsync(BuildAuthedGet("/api/members/me/certificates", token));
        var certificates = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var certificateId = certificates[0].GetProperty("id").GetGuid();

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/members/me/certificates/{certificateId}");
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var deleteResponse = await _client.SendAsync(deleteRequest);

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        var afterDelete = await _client.SendAsync(BuildAuthedGet("/api/members/me/certificates", token));
        var afterDeleteBody = await afterDelete.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, afterDeleteBody.GetArrayLength());
    }

    [Fact]
    public async Task DeleteUnknownCertificate_ReturnsNotFound()
    {
        var token = await RegisterAndLoginAsync();

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/members/me/certificates/{Guid.NewGuid()}");
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var deleteResponse = await _client.SendAsync(deleteRequest);

        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task UploadOversizedPdfCertificate_ReturnsBadRequest()
    {
        var token = await RegisterAndLoginAsync();
        var oversized = new byte[3 * 1024 * 1024];

        var response = await _client.SendAsync(
            BuildUploadRequest("/api/members/me/certificates", token, oversized, "big.pdf", "application/pdf"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadUnsupportedExtensionCertificate_ReturnsBadRequest()
    {
        var token = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(
            BuildUploadRequest("/api/members/me/certificates", token, Encoding.UTF8.GetBytes("hello"), "notes.txt", "text/plain"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_WithoutAuth_ReturnsUnauthorized()
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("bytes"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "cert.pdf");

        var response = await _client.PostAsync("/api/members/me/certificates", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanListAnotherMembersCertificates()
    {
        var memberToken = await RegisterAndLoginAsync();
        await _client.SendAsync(BuildUploadRequest("/api/members/me/certificates", memberToken, Encoding.UTF8.GetBytes("bytes"), "cert.pdf", "application/pdf"));

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
            civilStatus = (string?)null,
            address = (string?)null,
            mobileNumber = (string?)null,
            housePhone = (string?)null,
            website = (string?)null,
            facebookUrl = (string?)null,
            linkedInUrl = (string?)null,
            xUrl = (string?)null,
            instagramUrl = (string?)null,
            prcLicenseNo = (string?)null,
            ptrNumber = (string?)null,
            tin = (string?)null,
            chapter = Chapters.Ncr,
            employmentStatus = (string?)null,
            company = (string?)null,
            position = (string?)null,
            businessAddress = (string?)null,
            yearsOfPractice = (int?)null,
            specialization = (string?)null,
            skills = (string?)null,
            memberType = MemberTypes.Regular
        });
        var profileResponse = await _client.SendAsync(profileRequest);
        var profileBody = await profileResponse.Content.ReadFromJsonAsync<JsonElement>();
        var memberId = profileBody.GetProperty("id").GetGuid();

        var (_, adminToken) = await CreatePrivilegedUserAsync(RoleNames.Admin);
        var viewResponse = await _client.SendAsync(BuildAuthedGet($"/api/members/{memberId}/certificates", adminToken));
        var certificates = await viewResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, viewResponse.StatusCode);
        Assert.Equal(1, certificates.GetArrayLength());
    }

    private Task<(Guid UserId, string Token)> CreatePrivilegedUserAsync(string role) =>
        _client.CreatePrivilegedUserAsync(_userManager, role);
}
