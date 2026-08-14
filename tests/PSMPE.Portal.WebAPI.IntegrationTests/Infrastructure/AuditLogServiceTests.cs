using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Infrastructure.Persistence;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Infrastructure;

public class AuditLogServiceTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public AuditLogServiceTests(CustomWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RecordAsync_PersistsAllFields()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        await service.RecordAsync("membership.approved", actorId, "203.0.113.5", "Member", targetId, "{\"membershipNo\":\"000123\"}");

        // CustomWebApplicationFactory (and its InMemory database) is shared across every [Fact]
        // in this class via IClassFixture, so rows from other tests accumulate in the same table.
        // Filter to this test's own actorId (a fresh Guid per run) rather than asserting on the
        // whole table - same pattern MembersControllerTests uses for shared-fixture isolation.
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.AuditLogs.Where(a => a.ActorUserId == actorId));
        Assert.Equal("membership.approved", row.EventType);
        Assert.Equal(actorId, row.ActorUserId);
        Assert.Equal("203.0.113.5", row.ActorIp);
        Assert.Equal("Member", row.TargetType);
        Assert.Equal(targetId, row.TargetId);
        Assert.Equal("{\"membershipNo\":\"000123\"}", row.Metadata);
    }

    [Fact]
    public async Task RecordAsync_NullActorAndTarget_PersistsAsNull()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        await service.RecordAsync("auth.rate_limit.rejected", null, "203.0.113.9", null, null, null);

        // See the isolation note in RecordAsync_PersistsAllFields above - filter to this test's
        // own actor IP rather than asserting on the whole shared table.
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.AuditLogs.Where(a => a.ActorIp == "203.0.113.9" && a.EventType == "auth.rate_limit.rejected"));
        Assert.Null(row.ActorUserId);
        Assert.Null(row.TargetType);
        Assert.Null(row.TargetId);
        Assert.Null(row.Metadata);
    }

    [Fact]
    public async Task RecordAsync_WhenSaveFails_DoesNotThrow()
    {
        // The plan's original approach - db.Database.EnsureDeletedAsync() before calling the
        // service - was tried first and found to be a no-op on EF Core's InMemory provider: a
        // fresh SaveChangesAsync after EnsureDeletedAsync silently recreates the store and
        // succeeds, so that version of this test passed identically whether or not
        // AuditLogService caught anything, proving nothing. Disposing the context genuinely
        // breaks it - any subsequent use throws ObjectDisposedException - which is what actually
        // exercises the catch block below.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
        await db.DisposeAsync(); // breaks subsequent SaveChangesAsync calls on this context

        var exception = await Record.ExceptionAsync(() =>
            service.RecordAsync("auth.rate_limit.rejected", null, "203.0.113.9", null, null, null));

        Assert.Null(exception);
    }

    // GetPagedAsync's tests below run against the same shared IClassFixture database as every
    // other test in this class (and as each other - order across [Fact]s in a class isn't
    // guaranteed by xUnit). Asserting a literal TotalCount/Single on hardcoded event types like
    // "membership.approved" is provably unreliable here: empirically, running these tests as
    // originally written (fixed event types, no per-test marker) failed 3 of 4 with e.g.
    // "Expected: 3 Actual: 14" because rows accumulate across every call to SeedThreeRowsAsync
    // (each of the 4 tests below calls it once, so by the last test's turn the table already has
    // 3-9 unrelated rows from earlier tests, on top of whatever RecordAsync_* added). So each seed
    // call gets a fresh Guid marker suffixed onto EventType/Metadata, and every query below scopes
    // through that marker (via `search` or an exact `eventType` match) so it only ever sees rows
    // this test itself seeded - the same "unique-value-per-test, never assert on the whole shared
    // table" discipline the RecordAsync_* tests above already use with unique ActorIp/Guid values.
    private static async Task<string> SeedThreeRowsAsync(ApplicationDbContext db)
    {
        var marker = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        db.AuditLogs.AddRange(
            new AuditLog { EventType = $"auth.rate_limit.rejected.{marker}", ActorIp = "203.0.113.1", CreatedAt = now.AddDays(-1) },
            new AuditLog { EventType = $"auth.account.locked_out.{marker}", ActorIp = "203.0.113.2", CreatedAt = now.AddDays(-2) },
            new AuditLog
            {
                EventType = $"membership.approved.{marker}", TargetType = "Member", TargetId = Guid.NewGuid(),
                Metadata = $"{{\"membershipNo\":\"000999-{marker}\"}}", CreatedAt = now.AddDays(-3),
            });
        await db.SaveChangesAsync();
        return marker;
    }

    [Fact]
    public async Task GetPagedAsync_OrdersNewestFirst()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var marker = await SeedThreeRowsAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        var result = await service.GetPagedAsync(page: 1, pageSize: 20, search: marker, eventType: null, from: null, to: null);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal($"auth.rate_limit.rejected.{marker}", result.Items[0].EventType);
        Assert.Equal($"membership.approved.{marker}", result.Items[2].EventType);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByEventType()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var marker = await SeedThreeRowsAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        var result = await service.GetPagedAsync(
            page: 1, pageSize: 20, search: null, eventType: $"membership.approved.{marker}", from: null, to: null);

        var item = Assert.Single(result.Items);
        Assert.Equal($"membership.approved.{marker}", item.EventType);
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesMetadata()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var marker = await SeedThreeRowsAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        var result = await service.GetPagedAsync(
            page: 1, pageSize: 20, search: $"000999-{marker}", eventType: null, from: null, to: null);

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByDateRange()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var marker = await SeedThreeRowsAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        var result = await service.GetPagedAsync(
            page: 1, pageSize: 20, search: marker, eventType: null,
            from: DateTimeOffset.UtcNow.AddDays(-2).AddHours(-1), to: DateTimeOffset.UtcNow.AddDays(-1).AddHours(1));

        Assert.Equal(2, result.TotalCount); // excludes this test's own -3 day membership.approved row
    }
}
