using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Admin;

public class AdminChaptersControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminChaptersControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> TokenForAsync(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var (_, token) = await _client.CreatePrivilegedUserAsync(userManager, role);
        return token;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        return _client.SendAsync(request.WithBearer(token));
    }

    [Fact]
    public async Task AdminList_AsAdmin_Returns403()
    {
        var token = await TokenForAsync(RoleNames.Admin);

        var response = await SendAsync(HttpMethod.Get, "/api/admin/chapters", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminList_Anonymous_Returns401()
    {
        var response = await _client.GetAsync("/api/admin/chapters");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CanCreateSearchUpdateAndDeleteAChapter()
    {
        var token = await TokenForAsync(RoleNames.SuperAdmin);

        var create = await SendAsync(HttpMethod.Post, "/api/admin/chapters", token, new { name = "Iloilo Crud" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var list = await SendAsync(HttpMethod.Get, "/api/admin/chapters?search=iloilo", token);
        var rows = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, rows.GetArrayLength());

        var update = await SendAsync(HttpMethod.Put, $"/api/admin/chapters/{id}", token,
            new { name = "Iloilo Renamed", isActive = false });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Iloilo Renamed", updated.GetProperty("name").GetString());
        Assert.False(updated.GetProperty("isActive").GetBoolean());

        var inactive = await SendAsync(HttpMethod.Get, "/api/admin/chapters?isActive=false&search=renamed", token);
        Assert.Equal(1, (await inactive.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());

        var delete = await SendAsync(HttpMethod.Delete, $"/api/admin/chapters/{id}", token);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateName_Returns409()
    {
        var token = await TokenForAsync(RoleNames.SuperAdmin);

        var response = await SendAsync(HttpMethod.Post, "/api/admin/chapters", token, new { name = "ncr" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_BlankName_Returns400()
    {
        var token = await TokenForAsync(RoleNames.SuperAdmin);

        var response = await SendAsync(HttpMethod.Post, "/api/admin/chapters", token, new { name = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ChapterInUse_Returns409_AndMessageSuggestsDeactivating()
    {
        var token = await TokenForAsync(RoleNames.SuperAdmin);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PSMPE.Portal.Infrastructure.Persistence.ApplicationDbContext>();
            db.Events.Add(new Event { Title = "In use", Description = "d", ChapterId = TestChapters.Baguio });
            await db.SaveChangesAsync();
        }

        var response = await SendAsync(HttpMethod.Delete, $"/api/admin/chapters/{TestChapters.Baguio}", token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Deactivate", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PublicList_AsMember_ReturnsAllChaptersIncludingInactive()
    {
        var superAdminToken = await TokenForAsync(RoleNames.SuperAdmin);
        var created = await SendAsync(HttpMethod.Post, "/api/admin/chapters", superAdminToken,
            new { name = "Retired Chapter", isActive = false });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var memberToken = await TokenForAsync(RoleNames.Member);

        var response = await SendAsync(HttpMethod.Get, "/api/chapters", memberToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await response.Content.ReadFromJsonAsync<JsonElement>();
        var names = rows.EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToList();
        Assert.Contains("NCR", names);
        var retired = rows.EnumerateArray().Single(r => r.GetProperty("name").GetString() == "Retired Chapter");
        Assert.False(retired.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task PublicList_Anonymous_Returns401()
    {
        var response = await _client.GetAsync("/api/chapters");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
