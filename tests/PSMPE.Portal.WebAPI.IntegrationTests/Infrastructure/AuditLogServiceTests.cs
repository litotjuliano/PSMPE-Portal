using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Common.Interfaces;
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
}
