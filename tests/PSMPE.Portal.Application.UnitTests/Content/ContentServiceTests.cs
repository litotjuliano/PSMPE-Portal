using PSMPE.Portal.Application.Content;
using PSMPE.Portal.Application.Content.Dtos;
using PSMPE.Portal.Application.UnitTests.TestSupport;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Content;

public class ContentServiceTests
{
    [Fact]
    public async Task CreateAsync_SetsOwnerIdToCurrentUser()
    {
        using var db = TestDbContext.CreateInMemory();
        var ownerId = Guid.NewGuid();
        var service = new ContentService(db, new FakeCurrentUserService(ownerId, RoleNames.ContentCreator));

        var created = await service.CreateAsync(new CreateContentItemRequest("Title", "Body", null));

        Assert.Equal(ownerId, created.OwnerId);
    }

    [Fact]
    public async Task DeleteAsync_ByNonOwnerNonAdmin_ReturnsForbidden()
    {
        using var db = TestDbContext.CreateInMemory();
        var ownerId = Guid.NewGuid();
        var item = new ContentItem { Title = "T", Body = "B", OwnerId = ownerId };
        db.ContentItems.Add(item);
        await db.SaveChangesAsync();

        var service = new ContentService(db, new FakeCurrentUserService(Guid.NewGuid(), RoleNames.ContentCreator));

        var result = await service.DeleteAsync(item.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(Common.Models.ResultErrorType.Forbidden, result.ErrorType);
    }

    [Fact]
    public async Task DeleteAsync_ByOwner_Succeeds()
    {
        using var db = TestDbContext.CreateInMemory();
        var ownerId = Guid.NewGuid();
        var item = new ContentItem { Title = "T", Body = "B", OwnerId = ownerId };
        db.ContentItems.Add(item);
        await db.SaveChangesAsync();

        var service = new ContentService(db, new FakeCurrentUserService(ownerId, RoleNames.ContentCreator));

        var result = await service.DeleteAsync(item.Id);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task DeleteAsync_ByAdmin_SucceedsEvenWithoutOwnership()
    {
        using var db = TestDbContext.CreateInMemory();
        var ownerId = Guid.NewGuid();
        var item = new ContentItem { Title = "T", Body = "B", OwnerId = ownerId };
        db.ContentItems.Add(item);
        await db.SaveChangesAsync();

        var service = new ContentService(db, new FakeCurrentUserService(Guid.NewGuid(), RoleNames.Admin));

        var result = await service.DeleteAsync(item.Id);

        Assert.True(result.Succeeded);
    }
}
