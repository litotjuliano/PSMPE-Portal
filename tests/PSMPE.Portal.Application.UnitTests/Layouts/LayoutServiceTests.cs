using PSMPE.Portal.Application.Layouts;
using PSMPE.Portal.Application.UnitTests.TestSupport;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Layouts;

public class LayoutServiceTests
{
    [Fact]
    public async Task DeleteAsync_SystemLayout_ByAdmin_IsForbidden()
    {
        using var db = TestDbContext.CreateInMemory();
        var layout = new Layout { Name = "System", Definition = "{}", IsSystemLayout = true, OwnerId = null };
        db.Layouts.Add(layout);
        await db.SaveChangesAsync();

        var service = new LayoutService(db, new FakeCurrentUserService(Guid.NewGuid(), RoleNames.Admin));

        var result = await service.DeleteAsync(layout.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(Common.Models.ResultErrorType.Forbidden, result.ErrorType);
    }

    [Fact]
    public async Task DeleteAsync_SystemLayout_ByUserWithoutDeleteSystemPermission_IsForbidden()
    {
        using var db = TestDbContext.CreateInMemory();
        var layout = new Layout { Name = "System", Definition = "{}", IsSystemLayout = true, OwnerId = null };
        db.Layouts.Add(layout);
        await db.SaveChangesAsync();

        var service = new LayoutService(db, new FakeCurrentUserService(Guid.NewGuid(), RoleNames.SuperAdmin));

        var result = await service.DeleteAsync(layout.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(Common.Models.ResultErrorType.Forbidden, result.ErrorType);
    }

    [Fact]
    public async Task DeleteAsync_SystemLayout_ByUserWithDeleteSystemPermission_Succeeds()
    {
        using var db = TestDbContext.CreateInMemory();
        var layout = new Layout { Name = "System", Definition = "{}", IsSystemLayout = true, OwnerId = null };
        db.Layouts.Add(layout);
        await db.SaveChangesAsync();

        var service = new LayoutService(db, new FakeCurrentUserService(Guid.NewGuid(), RoleNames.SuperAdmin)
        {
            GrantedPermissions = [Permissions.Layout.DeleteSystem]
        });

        var result = await service.DeleteAsync(layout.Id);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task DeleteAsync_OwnNonSystemLayout_ByOwner_Succeeds()
    {
        using var db = TestDbContext.CreateInMemory();
        var ownerId = Guid.NewGuid();
        var layout = new Layout { Name = "Mine", Definition = "{}", IsSystemLayout = false, OwnerId = ownerId };
        db.Layouts.Add(layout);
        await db.SaveChangesAsync();

        var service = new LayoutService(db, new FakeCurrentUserService(ownerId, RoleNames.Member));

        var result = await service.DeleteAsync(layout.Id);

        Assert.True(result.Succeeded);
    }
}
