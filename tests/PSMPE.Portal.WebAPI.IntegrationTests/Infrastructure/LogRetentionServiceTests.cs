using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Infrastructure;

public class LogRetentionServiceTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public LogRetentionServiceTests(CustomWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PruneAsync_DeletesOldSecurityEvents_KeepsRecentOnes_NeverPrunesApprovals()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;

        var oldRejection = new AuditLog { EventType = "auth.rate_limit.rejected", CreatedAt = now.AddDays(-91) };
        var recentRejection = new AuditLog { EventType = "auth.account.locked_out", CreatedAt = now.AddDays(-89) };
        var oldApproval = new AuditLog { EventType = "membership.approved", CreatedAt = now.AddDays(-500) };
        db.AuditLogs.AddRange(oldRejection, recentRejection, oldApproval);
        await db.SaveChangesAsync();

        var retentionService = scope.ServiceProvider.GetRequiredService<ILogRetentionService>();
        await retentionService.PruneAsync();

        var remaining = db.AuditLogs.Select(a => a.Id).ToList();
        Assert.DoesNotContain(oldRejection.Id, remaining);
        Assert.Contains(recentRejection.Id, remaining);
        Assert.Contains(oldApproval.Id, remaining);
    }

    [Fact]
    public async Task PruneAsync_DeletesErrorLogRowsOlderThan30Days_KeepsRecentOnes()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;

        var old = new ErrorLog { Source = ErrorSource.Backend, Message = "old", CreatedAt = now.AddDays(-31) };
        var recent = new ErrorLog { Source = ErrorSource.Frontend, Message = "recent", CreatedAt = now.AddDays(-29) };
        db.ErrorLogs.AddRange(old, recent);
        await db.SaveChangesAsync();

        var retentionService = scope.ServiceProvider.GetRequiredService<ILogRetentionService>();
        await retentionService.PruneAsync();

        var remaining = db.ErrorLogs.Select(e => e.Id).ToList();
        Assert.DoesNotContain(old.Id, remaining);
        Assert.Contains(recent.Id, remaining);
    }
}
