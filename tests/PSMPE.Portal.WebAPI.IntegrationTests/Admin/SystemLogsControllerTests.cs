using System.Linq;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Admin;

public class SystemLogsControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SystemLogsControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetAuditLog_WithoutSuperAdmin_Returns403()
    {
        using var setupScope = _factory.Services.CreateScope();
        var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var (_, token) = await _client.CreatePrivilegedUserAsync(userManager, RoleNames.Admin);

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/admin/audit-log").WithBearer(token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAuditLog_AsSuperAdmin_ResolvesActorEmail()
    {
        using var setupScope = _factory.Services.CreateScope();
        var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var (_, token) = await _client.CreatePrivilegedUserAsync(userManager, RoleNames.SuperAdmin);

        var actor = new ApplicationUser { UserName = "actor@example.com", Email = "actor@example.com", DisplayName = "Actor", EmailConfirmed = true };
        await userManager.CreateAsync(actor, "Password123!");

        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AuditLogs.Add(new AuditLog
            {
                EventType = "membership.approved", ActorUserId = actor.Id, TargetType = "Member", TargetId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/admin/audit-log").WithBearer(token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var item = body.GetProperty("items")[0];
        Assert.Equal("actor@example.com", item.GetProperty("actorEmail").GetString());
    }

    [Fact]
    public async Task GetAuditLog_AsSuperAdmin_NullActor_ReturnsNullActorEmail()
    {
        using var setupScope = _factory.Services.CreateScope();
        var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var (_, token) = await _client.CreatePrivilegedUserAsync(userManager, RoleNames.SuperAdmin);

        const string markerIp = "203.0.113.42";

        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AuditLogs.Add(new AuditLog
            {
                EventType = "auth.rate_limit.rejected", ActorUserId = null, ActorIp = markerIp,
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/admin/audit-log").WithBearer(token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        // Shares the class fixture's database with the other tests in this class, and results are
        // ordered newest-first - so index [0] isn't reliably this test's own row. Find it by the
        // marker IP instead of assuming position.
        var item = body.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("actorIp").GetString() == markerIp);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, item.GetProperty("actorEmail").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, item.GetProperty("actorUserId").ValueKind);
    }
}
