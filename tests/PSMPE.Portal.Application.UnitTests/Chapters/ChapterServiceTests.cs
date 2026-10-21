using PSMPE.Portal.Application.Chapters;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.UnitTests.TestSupport;
using PSMPE.Portal.Domain.Entities;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Chapters;

public class ChapterServiceTests
{
    // TestDbContext.CreateInMemory() already holds the six starting chapters.
    private static (TestDbContext Db, ChapterService Service) Create()
    {
        var db = TestDbContext.CreateInMemory();
        return (db, new ChapterService(db));
    }

    private static Member BuildMember(Guid chapterId) => new()
    {
        UserId = Guid.NewGuid(), FirstName = "Juan", LastName = "Dela Cruz", ChapterId = chapterId,
    };

    [Fact]
    public async Task GetAll_ReturnsChaptersInSortOrderWithUsageCounts()
    {
        var (db, service) = Create();
        db.Members.Add(BuildMember(TestChapters.Cebu));
        db.Members.Add(BuildMember(TestChapters.Cebu));
        db.Events.Add(new Event { Title = "Seminar", Description = "d", ChapterId = TestChapters.Cebu });
        await db.SaveChangesAsync();

        var rows = await service.GetAllAsync();

        Assert.Equal(TestChapters.AllNames, rows.Select(r => r.Name));
        var cebu = rows.Single(r => r.Name == TestChapters.CebuName);
        Assert.Equal(2, cebu.MemberCount);
        Assert.Equal(1, cebu.EventCount);
        Assert.Equal(0, rows.Single(r => r.Name == TestChapters.NcrName).MemberCount);
    }

    [Fact]
    public async Task GetAll_FiltersBySearchAndActiveState()
    {
        var (db, service) = Create();
        (await db.Chapters.FindAsync(TestChapters.Davao))!.IsActive = false;
        await db.SaveChangesAsync();

        var searched = await service.GetAllAsync(search: "  CEB ");
        var inactive = await service.GetAllAsync(isActive: false);
        var active = await service.GetAllAsync(isActive: true);

        Assert.Equal([TestChapters.CebuName], searched.Select(r => r.Name));
        Assert.Equal([TestChapters.DavaoName], inactive.Select(r => r.Name));
        Assert.Equal(5, active.Count);
    }

    [Fact]
    public async Task Create_TrimsNameAppendsSortOrderAndDefaultsToActive()
    {
        var (_, service) = Create();

        var result = await service.CreateAsync(new SaveChapterRequest("  Iloilo ", null, null));

        Assert.True(result.Succeeded);
        Assert.Equal("Iloilo", result.Value!.Name);
        Assert.Equal(7, result.Value.SortOrder);
        Assert.True(result.Value.IsActive);
        Assert.Contains(await service.GetAllAsync(), r => r.Name == "Iloilo");
    }

    [Fact]
    public async Task Create_DuplicateNameIgnoringCase_Conflicts()
    {
        var (_, service) = Create();

        var result = await service.CreateAsync(new SaveChapterRequest("cebu", null, null));

        Assert.False(result.Succeeded);
        Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_BlankName_Fails(string name)
    {
        var (_, service) = Create();

        var result = await service.CreateAsync(new SaveChapterRequest(name, null, null));

        Assert.False(result.Succeeded);
        Assert.Equal(ResultErrorType.Validation, result.ErrorType);
    }

    [Fact]
    public async Task Create_NameOverSixtyFourCharacters_Fails()
    {
        var (_, service) = Create();

        var result = await service.CreateAsync(new SaveChapterRequest(new string('x', 65), null, null));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Update_RenamesInPlace_SoMembersFollowTheNewName()
    {
        var (db, service) = Create();
        db.Members.Add(BuildMember(TestChapters.Baguio));
        await db.SaveChangesAsync();

        var result = await service.UpdateAsync(TestChapters.Baguio, new SaveChapterRequest("Cordillera", null, null));

        Assert.True(result.Succeeded);
        Assert.Equal("Cordillera", result.Value!.Name);
        Assert.Equal(1, result.Value.MemberCount);
        Assert.Equal(4, result.Value.SortOrder);
    }

    [Fact]
    public async Task Update_ToAnotherChaptersName_Conflicts()
    {
        var (_, service) = Create();

        var result = await service.UpdateAsync(TestChapters.Baguio, new SaveChapterRequest("NCR", null, null));

        Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task Update_KeepingItsOwnName_Succeeds()
    {
        var (_, service) = Create();

        var result = await service.UpdateAsync(TestChapters.Baguio, new SaveChapterRequest("baguio", null, null));

        Assert.True(result.Succeeded);
        Assert.Equal("baguio", result.Value!.Name);
    }

    [Fact]
    public async Task Update_TogglesActiveOnlyWhenSupplied()
    {
        var (_, service) = Create();

        var deactivated = await service.UpdateAsync(TestChapters.Cavite, new SaveChapterRequest("Cavite", null, false));
        var renamedOnly = await service.UpdateAsync(TestChapters.Cavite, new SaveChapterRequest("Cavite Province", null, null));

        Assert.False(deactivated.Value!.IsActive);
        Assert.False(renamedOnly.Value!.IsActive);
    }

    [Fact]
    public async Task Update_UnknownId_NotFound()
    {
        var (_, service) = Create();

        var result = await service.UpdateAsync(Guid.NewGuid(), new SaveChapterRequest("X", null, null));

        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task Delete_UnusedChapter_Succeeds()
    {
        var (_, service) = Create();

        var result = await service.DeleteAsync(TestChapters.QuezonCity);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(await service.GetAllAsync(), r => r.Id == TestChapters.QuezonCity);
    }

    [Fact]
    public async Task Delete_ChapterWithMembers_ConflictsAndSuggestsDeactivating()
    {
        var (db, service) = Create();
        db.Members.Add(BuildMember(TestChapters.Ncr));
        await db.SaveChangesAsync();

        var result = await service.DeleteAsync(TestChapters.Ncr);

        Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
        Assert.Contains("Deactivate", result.Error);
        Assert.Contains(await service.GetAllAsync(), r => r.Id == TestChapters.Ncr);
    }

    [Fact]
    public async Task Delete_ChapterWithEvents_Conflicts()
    {
        var (db, service) = Create();
        db.Events.Add(new Event { Title = "Seminar", Description = "d", ChapterId = TestChapters.Davao });
        await db.SaveChangesAsync();

        var result = await service.DeleteAsync(TestChapters.Davao);

        Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task Delete_UnknownId_NotFound()
    {
        var (_, service) = Create();

        var result = await service.DeleteAsync(Guid.NewGuid());

        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
    }
}
