using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Admin;

public class AdminLocationsControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminLocationsControllerTests(CustomWebApplicationFactory factory)
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

    private async Task<Guid> SeedProvinceAsync(string suffix)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var region = new PhRegion { Name = $"Test Region {suffix}", SortOrder = 1 };
        var province = new PhProvince { Name = $"Test Province {suffix}", Region = region };
        db.PhProvinces.Add(province);
        await db.SaveChangesAsync();
        return province.Id;
    }

    [Fact]
    public async Task Cities_AsAdmin_Returns403()
    {
        var token = await TokenForAsync(RoleNames.Admin);

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/admin/locations/cities").WithBearer(token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cities_Anonymous_Returns401()
    {
        var response = await _client.GetAsync("/api/admin/locations/cities");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CanCreateListUpdateAndDeleteACity()
    {
        var token = await TokenForAsync(RoleNames.SuperAdmin);
        var provinceId = await SeedProvinceAsync("crud");

        var create = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/admin/locations/cities")
        { Content = JsonContent.Create(new { name = "Newtown", zipCode = "1234", provinceId }) }.WithBearer(token));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var list = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get,
            $"/api/admin/locations/cities?provinceId={provinceId}&search=newt").WithBearer(token));
        var body = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());

        var update = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Put, $"/api/admin/locations/cities/{id}")
        { Content = JsonContent.Create(new { name = "Newtown City", zipCode = (string?)null, provinceId }) }.WithBearer(token));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var delete = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/locations/cities/{id}").WithBearer(token));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_DeletingAProvinceWithCities_Returns409()
    {
        var token = await TokenForAsync(RoleNames.SuperAdmin);
        var provinceId = await SeedProvinceAsync("conflict");
        await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/admin/locations/cities")
        { Content = JsonContent.Create(new { name = "Keeper", zipCode = (string?)null, provinceId }) }.WithBearer(token));

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/locations/provinces/{provinceId}").WithBearer(token));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_DuplicateCity_Returns409()
    {
        var token = await TokenForAsync(RoleNames.SuperAdmin);
        var provinceId = await SeedProvinceAsync("dup");
        HttpRequestMessage Post() => new HttpRequestMessage(HttpMethod.Post, "/api/admin/locations/cities")
        { Content = JsonContent.Create(new { name = "Twin", zipCode = (string?)null, provinceId }) }.WithBearer(token);

        await _client.SendAsync(Post());
        var second = await _client.SendAsync(Post());

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task GetLocations_AsMember_ReturnsFlatRows()
    {
        var token = await TokenForAsync(RoleNames.Member);
        var provinceId = await SeedProvinceAsync("read");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.PhCities.Add(new PhCity { Name = "Readable", ZipCode = "9999", ProvinceId = provinceId });
            await db.SaveChangesAsync();
        }

        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/locations").WithBearer(token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(rows.EnumerateArray(), r =>
            r.GetProperty("city").GetString() == "Readable" && r.GetProperty("zipCode").GetString() == "9999");
    }

    [Fact]
    public async Task GetLocations_Anonymous_Returns401()
    {
        var response = await _client.GetAsync("/api/locations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
