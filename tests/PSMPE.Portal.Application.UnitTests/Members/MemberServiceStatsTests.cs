using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Application.UnitTests.TestSupport;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Members;

/// <summary>
/// Covers MemberService.GetStatsAsync, the aggregation endpoint backing the admin Membership
/// dashboard. See MemberServiceTests.cs for the general MemberService test conventions this
/// follows (TestDbContext.CreateInMemory, plain `new MemberService(db)`).
/// </summary>
public class MemberServiceStatsTests
{
    private static Member BuildMember(
        MembershipStatus status = MembershipStatus.Pending,
        string chapter = Chapters.Ncr,
        string memberType = MemberTypes.Regular,
        DateTimeOffset? submittedAt = null,
        DateOnly? renewalDueDate = null,
        DateTimeOffset? approvedAt = null,
        bool prcIdVerified = false,
        string? prcLicenseNo = null,
        string? pendingPrcLicenseNo = null) => new()
    {
        UserId = Guid.NewGuid(),
        User = new ApplicationUser { UserName = $"{Guid.NewGuid()}@example.com", Email = $"{Guid.NewGuid()}@example.com" },
        FirstName = "Juan",
        LastName = "Dela Cruz",
        Chapter = chapter,
        MemberType = memberType,
        Status = status,
        SubmittedAt = submittedAt,
        RenewalDueDate = renewalDueDate,
        ApprovedAt = approvedAt,
        PrcIdVerified = prcIdVerified,
        PrcLicenseNo = prcLicenseNo,
        PendingPrcLicenseNo = pendingPrcLicenseNo,
    };

    [Fact]
    public async Task GetStatsAsync_CountsEachStatus_AndZeroFillsStatusesWithNoMembers()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        db.Members.AddRange(
            BuildMember(status: MembershipStatus.Pending, submittedAt: DateTimeOffset.UtcNow),
            BuildMember(status: MembershipStatus.Pending, submittedAt: DateTimeOffset.UtcNow),
            BuildMember(status: MembershipStatus.Active, submittedAt: DateTimeOffset.UtcNow));
        // Expired and Deactivated deliberately have zero members.
        await db.SaveChangesAsync();

        var stats = await service.GetStatsAsync(null);

        Assert.Equal(2, stats.StatusCounts.Pending);
        Assert.Equal(1, stats.StatusCounts.Active);
        Assert.Equal(0, stats.StatusCounts.Expired);
        Assert.Equal(0, stats.StatusCounts.Deactivated);
    }

    [Fact]
    public async Task GetStatsAsync_ExcludesDraftMembers_WithNoSubmittedAt()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        db.Members.AddRange(
            BuildMember(status: MembershipStatus.Pending, submittedAt: DateTimeOffset.UtcNow),
            BuildMember(status: MembershipStatus.Pending, submittedAt: null)); // draft
        await db.SaveChangesAsync();

        var stats = await service.GetStatsAsync(null);

        Assert.Equal(1, stats.StatusCounts.Pending);
    }

    [Fact]
    public async Task GetStatsAsync_ExcludesGivenUserIds_FromStatusCounts()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        // Captured from the saved entity, not a pre-generated Guid: EF's relationship fixup
        // overwrites Member.UserId to match the ApplicationUser's own generated Id on save (same
        // reason MemberServiceTests.cs's GetAllAsync_WithExcludeUserIds_... reads `excluded.UserId`
        // only after SaveChangesAsync).
        var excluded = BuildMember(status: MembershipStatus.Active, submittedAt: DateTimeOffset.UtcNow);
        db.Members.AddRange(
            excluded,
            BuildMember(status: MembershipStatus.Active, submittedAt: DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var stats = await service.GetStatsAsync([excluded.UserId]);

        Assert.Equal(1, stats.StatusCounts.Active);
    }

    [Fact]
    public async Task GetStatsAsync_RegistrationTrend_HasTwelveMonthsOldestFirst_ZeroFilledForMonthsWithNoSubmissions()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var now = DateTimeOffset.UtcNow;
        db.Members.AddRange(
            BuildMember(submittedAt: now),
            BuildMember(submittedAt: now),
            BuildMember(submittedAt: now.AddMonths(-2)));
        await db.SaveChangesAsync();

        var stats = await service.GetStatsAsync(null);

        Assert.Equal(12, stats.RegistrationTrend.Count);
        // Oldest first: the first entry is 11 months before the current month.
        var expectedOldest = new DateTime(now.Year, now.Month, 1).AddMonths(-11);
        Assert.Equal(expectedOldest.Year, stats.RegistrationTrend[0].Year);
        Assert.Equal(expectedOldest.Month, stats.RegistrationTrend[0].Month);
        // Most recent entry is the current month.
        Assert.Equal(now.Year, stats.RegistrationTrend[^1].Year);
        Assert.Equal(now.Month, stats.RegistrationTrend[^1].Month);
        Assert.Equal(2, stats.RegistrationTrend[^1].Count);

        var twoMonthsAgo = now.AddMonths(-2);
        var twoMonthsAgoEntry = stats.RegistrationTrend.Single(r => r.Year == twoMonthsAgo.Year && r.Month == twoMonthsAgo.Month);
        Assert.Equal(1, twoMonthsAgoEntry.Count);

        // A month with no submissions at all (e.g. 5 months ago) must still appear, zero-filled.
        var fiveMonthsAgo = now.AddMonths(-5);
        var fiveMonthsAgoEntry = stats.RegistrationTrend.Single(r => r.Year == fiveMonthsAgo.Year && r.Month == fiveMonthsAgo.Month);
        Assert.Equal(0, fiveMonthsAgoEntry.Count);
    }

    [Fact]
    public async Task GetStatsAsync_RegistrationTrend_ExcludesSubmissionsOlderThanTwelveMonths()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var now = DateTimeOffset.UtcNow;
        db.Members.Add(BuildMember(submittedAt: now.AddMonths(-13)));
        await db.SaveChangesAsync();

        var stats = await service.GetStatsAsync(null);

        Assert.Equal(0, stats.RegistrationTrend.Sum(r => r.Count));
    }

    [Fact]
    public async Task GetStatsAsync_ByChapter_ZeroFillsChaptersWithNoMembers_InDeclaredOrder()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        db.Members.AddRange(
            BuildMember(chapter: Chapters.Cebu, submittedAt: DateTimeOffset.UtcNow),
            BuildMember(chapter: Chapters.Cebu, submittedAt: DateTimeOffset.UtcNow),
            BuildMember(chapter: Chapters.Davao, submittedAt: DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var stats = await service.GetStatsAsync(null);

        Assert.Equal(Chapters.All, stats.ByChapter.Select(c => c.Name).ToArray());
        Assert.Equal(0, stats.ByChapter.Single(c => c.Name == Chapters.Ncr).Count);
        Assert.Equal(2, stats.ByChapter.Single(c => c.Name == Chapters.Cebu).Count);
        Assert.Equal(1, stats.ByChapter.Single(c => c.Name == Chapters.Davao).Count);
        Assert.Equal(0, stats.ByChapter.Single(c => c.Name == Chapters.Baguio).Count);
        Assert.Equal(0, stats.ByChapter.Single(c => c.Name == Chapters.Cavite).Count);
        Assert.Equal(0, stats.ByChapter.Single(c => c.Name == Chapters.QuezonCity).Count);
    }

    [Fact]
    public async Task GetStatsAsync_ByMemberType_ZeroFillsTypesWithNoMembers_InDeclaredOrder()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        db.Members.Add(BuildMember(memberType: MemberTypes.SeniorCitizen, submittedAt: DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var stats = await service.GetStatsAsync(null);

        Assert.Equal(MemberTypes.All, stats.ByMemberType.Select(t => t.Name).ToArray());
        Assert.Equal(0, stats.ByMemberType.Single(t => t.Name == MemberTypes.Regular).Count);
        Assert.Equal(0, stats.ByMemberType.Single(t => t.Name == MemberTypes.New).Count);
        Assert.Equal(0, stats.ByMemberType.Single(t => t.Name == MemberTypes.Renewal).Count);
        Assert.Equal(1, stats.ByMemberType.Single(t => t.Name == MemberTypes.SeniorCitizen).Count);
    }

    [Fact]
    public async Task GetStatsAsync_ActionItems_PendingApprovals_CountsMembersWithNoApprovedAt()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        db.Members.AddRange(
            BuildMember(submittedAt: DateTimeOffset.UtcNow, approvedAt: null),
            BuildMember(submittedAt: DateTimeOffset.UtcNow, approvedAt: null),
            BuildMember(submittedAt: DateTimeOffset.UtcNow, approvedAt: DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var stats = await service.GetStatsAsync(null);

        Assert.Equal(2, stats.ActionItems.PendingApprovals);
    }

    [Fact]
    public async Task GetStatsAsync_ActionItems_PendingPrcVerification_IncludesNeverVerifiedAndPendingChange_ExcludesVerified()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        db.Members.AddRange(
            // Never verified: has a PrcLicenseNo, not yet verified, nothing pending.
            BuildMember(submittedAt: DateTimeOffset.UtcNow, prcIdVerified: false, prcLicenseNo: "MP-1", pendingPrcLicenseNo: null),
            // Verified but with a proposed change pending review.
            BuildMember(submittedAt: DateTimeOffset.UtcNow, prcIdVerified: true, prcLicenseNo: "MP-2", pendingPrcLicenseNo: "MP-2-NEW"),
            // Fully verified, nothing pending - should not count.
            BuildMember(submittedAt: DateTimeOffset.UtcNow, prcIdVerified: true, prcLicenseNo: "MP-3", pendingPrcLicenseNo: null),
            // No PrcLicenseNo at all and not verified - GetAllAsync's predicate also excludes this
            // (nothing to verify), so it must not count here either.
            BuildMember(submittedAt: DateTimeOffset.UtcNow, prcIdVerified: false, prcLicenseNo: null, pendingPrcLicenseNo: null));
        await db.SaveChangesAsync();

        var stats = await service.GetStatsAsync(null);

        Assert.Equal(2, stats.ActionItems.PendingPrcVerification);
    }

    [Fact]
    public async Task GetStatsAsync_ActionItems_RenewalsDueSoon_MemberDueIn59Days_Counts()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(59);
        db.Members.Add(BuildMember(status: MembershipStatus.Active, submittedAt: DateTimeOffset.UtcNow, renewalDueDate: dueDate));
        await db.SaveChangesAsync();

        var stats = await service.GetStatsAsync(null);

        Assert.Equal(1, stats.ActionItems.RenewalsDueSoon);
    }

    [Fact]
    public async Task GetStatsAsync_ActionItems_RenewalsDueSoon_MemberDueIn61Days_DoesNotCount()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(61);
        db.Members.Add(BuildMember(status: MembershipStatus.Active, submittedAt: DateTimeOffset.UtcNow, renewalDueDate: dueDate));
        await db.SaveChangesAsync();

        var stats = await service.GetStatsAsync(null);

        Assert.Equal(0, stats.ActionItems.RenewalsDueSoon);
    }

    [Fact]
    public async Task GetStatsAsync_ActionItems_RenewalsDueSoon_OnlyCountsActiveMembers()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
        db.Members.Add(BuildMember(status: MembershipStatus.Pending, submittedAt: DateTimeOffset.UtcNow, renewalDueDate: dueDate));
        await db.SaveChangesAsync();

        var stats = await service.GetStatsAsync(null);

        Assert.Equal(0, stats.ActionItems.RenewalsDueSoon);
    }

    /// <summary>Exercises the upper bound at exactly RenewalDueSoonDays (60) - the comparison is
    /// &lt;=, so this must still count, not just values strictly inside the window.</summary>
    [Fact]
    public async Task GetStatsAsync_ActionItems_RenewalsDueSoon_MemberDueInExactly60Days_Counts()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(60);
        db.Members.Add(BuildMember(status: MembershipStatus.Active, submittedAt: DateTimeOffset.UtcNow, renewalDueDate: dueDate));
        await db.SaveChangesAsync();

        var stats = await service.GetStatsAsync(null);

        Assert.Equal(1, stats.ActionItems.RenewalsDueSoon);
    }

    /// <summary>Exercises the lower bound at exactly today (0 days out) - the comparison is
    /// &gt;=, so a renewal due today must still count.</summary>
    [Fact]
    public async Task GetStatsAsync_ActionItems_RenewalsDueSoon_MemberDueToday_Counts()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow);
        db.Members.Add(BuildMember(status: MembershipStatus.Active, submittedAt: DateTimeOffset.UtcNow, renewalDueDate: dueDate));
        await db.SaveChangesAsync();

        var stats = await service.GetStatsAsync(null);

        Assert.Equal(1, stats.ActionItems.RenewalsDueSoon);
    }

    /// <summary>The lower bound must actually exclude past-due dates, not just accept everything up
    /// to the upper bound - a member overdue by even one day is no longer "due soon", they're
    /// already lapsed.</summary>
    [Fact]
    public async Task GetStatsAsync_ActionItems_RenewalsDueSoon_MemberOverdueByOneDay_DoesNotCount()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        db.Members.Add(BuildMember(status: MembershipStatus.Active, submittedAt: DateTimeOffset.UtcNow, renewalDueDate: dueDate));
        await db.SaveChangesAsync();

        var stats = await service.GetStatsAsync(null);

        Assert.Equal(0, stats.ActionItems.RenewalsDueSoon);
    }
}
