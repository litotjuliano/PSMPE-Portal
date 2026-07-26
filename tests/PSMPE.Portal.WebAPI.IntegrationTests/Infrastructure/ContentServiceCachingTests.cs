using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Content;
using PSMPE.Portal.Application.Content.Dtos;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.Infrastructure.Services;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Infrastructure;

/// <summary>
/// End-to-end check that ContentService + the real MemoryCacheService (not the NoOp fallback
/// Application.UnitTests gets) actually cache and invalidate correctly when wired together via
/// real DI-shaped dependencies - complements the fine-grained MemoryCacheServiceTests and the
/// pure-business-logic ContentServiceTests (which use NoOp caching and wouldn't catch a caching
/// bug at all).
/// </summary>
public class ContentServiceCachingTests
{
    private sealed class FixedUserService(Guid userId, string role) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string r) => r == role;
        public bool HasPermission(string permission) => true;
    }

    private static (ApplicationDbContext Db, ContentService Service) CreateSut()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(options);

        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var cache = new MemoryCacheService(memoryCache, configuration);

        var service = new ContentService(db, new FixedUserService(Guid.NewGuid(), RoleNames.Admin), cache);
        return (db, service);
    }

    [Fact]
    public async Task GetAllAsync_CachesResult_ExternalDbChangeNotVisibleUntilInvalidated()
    {
        var (db, service) = CreateSut();
        db.ContentItems.Add(new ContentItem { Title = "First", Body = "Body", OwnerId = Guid.NewGuid(), Status = ContentStatus.Draft });
        await db.SaveChangesAsync();

        var firstRead = await service.GetAllAsync();
        Assert.Single(firstRead);

        // Bypasses ContentService entirely - simulates data changing without going through the
        // cached service (e.g. a direct DB edit) - the cached "all" list should NOT see this yet.
        db.ContentItems.Add(new ContentItem { Title = "Second", Body = "Body", OwnerId = Guid.NewGuid(), Status = ContentStatus.Draft });
        await db.SaveChangesAsync();

        var secondRead = await service.GetAllAsync();
        Assert.Single(secondRead); // still cached - proves caching is actually happening, not just correctness
    }

    [Fact]
    public async Task CreateAsync_InvalidatesTheAllCache_SoTheNewItemIsImmediatelyVisible()
    {
        var (_, service) = CreateSut();

        var beforeCreate = await service.GetAllAsync();
        Assert.Empty(beforeCreate);

        await service.CreateAsync(new CreateContentItemRequest("Title", "Body", null));

        var afterCreate = await service.GetAllAsync();
        Assert.Single(afterCreate); // Create invalidated the cache - no staleness
    }

    [Fact]
    public async Task UpdateAsync_InvalidatesBothTheAllCacheAndTheSingleItemCache()
    {
        var (_, service) = CreateSut();
        var created = await service.CreateAsync(new CreateContentItemRequest("Original", "Body", null));

        // Warm both caches before mutating.
        await service.GetAllAsync();
        await service.GetByIdAsync(created.Id);

        var updateResult = await service.UpdateAsync(created.Id, new UpdateContentItemRequest("Updated", "Body", ContentStatus.Published, null));
        Assert.True(updateResult.Succeeded);

        var byIdAfterUpdate = await service.GetByIdAsync(created.Id);
        var allAfterUpdate = await service.GetAllAsync();

        Assert.Equal("Updated", byIdAfterUpdate!.Title);
        Assert.Equal("Updated", Assert.Single(allAfterUpdate).Title);
    }
}
