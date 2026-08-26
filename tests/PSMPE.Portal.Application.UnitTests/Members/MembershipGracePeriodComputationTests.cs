using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Application.UnitTests.TestSupport;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Members;

/// <summary>
/// Covers MemberService's IsExpired/IsInGracePeriod computation after it was reworked to read
/// purely from RenewalDueDate + grace period days (needed so the flags stay correct once
/// MembershipLifecycleService starts auto-flipping Status to Expired), while still excluding
/// Deactivated members (a distinct admin action, not a lapsed-payment state).
/// </summary>
public class MembershipGracePeriodComputationTests
{
    private const int GraceDays = 7;

    private static async Task SeedGracePeriodConfigAsync(TestDbContext db)
    {
        db.SystemConfigs.Add(new SystemConfig { Key = "MembershipGracePeriodDays", Value = GraceDays.ToString() });
        await db.SaveChangesAsync();
    }

    private static Member BuildMember(MembershipStatus status, DateOnly? renewalDueDate) => new()
    {
        UserId = Guid.NewGuid(),
        User = new ApplicationUser { UserName = $"{Guid.NewGuid()}@example.com", Email = $"{Guid.NewGuid()}@example.com" },
        FirstName = "Juan",
        LastName = "Dela Cruz",
        Chapter = Chapters.Ncr,
        MemberType = MemberTypes.Regular,
        Status = status,
        SubmittedAt = DateTimeOffset.UtcNow.AddYears(-1),
        RenewalDueDate = renewalDueDate,
    };

    [Theory]
    // Boundary table around a 7-day grace period, for an Active member.
    [InlineData(10, false, false)] // due in the future - neither
    [InlineData(0, false, false)] // due today - not lapsed yet
    [InlineData(-1, true, false)] // first day past due - in grace
    [InlineData(-7, true, false)] // last day of grace
    [InlineData(-8, false, true)] // first day past grace - expired
    [InlineData(-40, false, true)] // well past grace - still expired
    public async Task ActiveMember_GracePeriodAndExpiredFlags_FollowRenewalDueDateBoundary(
        int dueDateOffsetDays, bool expectedInGrace, bool expectedExpired)
    {
        using var db = TestDbContext.CreateInMemory();
        await SeedGracePeriodConfigAsync(db);
        var service = new MemberService(db);
        var member = BuildMember(MembershipStatus.Active, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(dueDateOffsetDays));
        db.Members.Add(member);
        await db.SaveChangesAsync();

        var dto = await service.GetByIdAsync(member.Id);

        Assert.Equal(expectedInGrace, dto!.IsInGracePeriod);
        Assert.Equal(expectedExpired, dto.IsExpired);
    }

    [Fact]
    public async Task ExpiredStatusMember_StillComputesIsExpiredTrue_PastGraceWindow()
    {
        // Proves the flags no longer depend on Status == Active - needed once
        // MembershipLifecycleService starts persisting Status = Expired.
        using var db = TestDbContext.CreateInMemory();
        await SeedGracePeriodConfigAsync(db);
        var service = new MemberService(db);
        var member = BuildMember(MembershipStatus.Expired, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-40));
        db.Members.Add(member);
        await db.SaveChangesAsync();

        var dto = await service.GetByIdAsync(member.Id);

        Assert.True(dto!.IsExpired);
        Assert.False(dto.IsInGracePeriod);
    }

    [Fact]
    public async Task DeactivatedMember_AlwaysReadsFalseForBoth_RegardlessOfHowStaleTheDueDateIs()
    {
        using var db = TestDbContext.CreateInMemory();
        await SeedGracePeriodConfigAsync(db);
        var service = new MemberService(db);
        var member = BuildMember(MembershipStatus.Deactivated, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-400));
        db.Members.Add(member);
        await db.SaveChangesAsync();

        var dto = await service.GetByIdAsync(member.Id);

        Assert.False(dto!.IsExpired);
        Assert.False(dto.IsInGracePeriod);
    }

    [Fact]
    public async Task MemberWithNoRenewalDueDate_AlwaysReadsFalseForBoth()
    {
        using var db = TestDbContext.CreateInMemory();
        await SeedGracePeriodConfigAsync(db);
        var service = new MemberService(db);
        var member = BuildMember(MembershipStatus.Active, renewalDueDate: null);
        db.Members.Add(member);
        await db.SaveChangesAsync();

        var dto = await service.GetByIdAsync(member.Id);

        Assert.False(dto!.IsExpired);
        Assert.False(dto.IsInGracePeriod);
    }
}
