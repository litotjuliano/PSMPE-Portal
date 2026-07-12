using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Application.Members.Dtos;
using PSMPE.Portal.Application.UnitTests.TestSupport;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Members;

public class MemberServiceTests
{
    private static UpdateMyProfileRequest BuildRequest(
        string chapter = Chapters.Ncr, string memberType = MemberTypes.Regular,
        string? prcLicenseNo = null, bool prcIdReuploaded = false) => new(
        FirstName: "Juan", MiddleName: null, LastName: "Dela Cruz", Suffix: null,
        Birthdate: null, Gender: null, Address: null,
        PrcLicenseNo: prcLicenseNo, Chapter: chapter, Company: null, MemberType: memberType,
        PrcIdReuploaded: prcIdReuploaded);

    private static async Task<Member> SeedDraftMemberAsync(TestDbContext db, string? prcLicenseNo = null)
    {
        var member = new Member
        {
            UserId = Guid.NewGuid(),
            User = new ApplicationUser { UserName = $"{Guid.NewGuid()}@example.com", Email = $"{Guid.NewGuid()}@example.com" },
            MembershipNo = "000001",
            FirstName = "Juan",
            LastName = "Dela Cruz",
            Chapter = Chapters.Ncr,
            MemberType = MemberTypes.Regular,
            PrcLicenseNo = prcLicenseNo,
            Status = MembershipStatus.Pending,
            SubmittedAt = null,
        };
        db.Members.Add(member);
        await db.SaveChangesAsync();
        return member;
    }

    private static async Task<Member> SeedSubmittedMemberAsync(TestDbContext db, string? prcLicenseNo = null, DateTimeOffset? updatedAt = null)
    {
        var member = new Member
        {
            UserId = Guid.NewGuid(),
            User = new ApplicationUser { UserName = $"{Guid.NewGuid()}@example.com", Email = $"{Guid.NewGuid()}@example.com" },
            MembershipNo = "000001",
            FirstName = "Juan",
            LastName = "Dela Cruz",
            Chapter = Chapters.Ncr,
            MemberType = MemberTypes.Regular,
            PrcLicenseNo = prcLicenseNo,
            Status = MembershipStatus.Pending,
            SubmittedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow.AddDays(-1),
        };
        db.Members.Add(member);
        await db.SaveChangesAsync();
        return member;
    }

    [Fact]
    public async Task UpsertMyProfileAsync_DuringDraft_AllowsMemberTypeAndChapterChanges()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequest(chapter: Chapters.Cebu, memberType: MemberTypes.Regular));

        Assert.True(result.Succeeded);
        Assert.Equal(Chapters.Cebu, result.Value!.Chapter);
    }

    [Fact]
    public async Task UpsertMyProfileAsync_AfterSubmit_RejectsMemberTypeAndChapterChange()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedSubmittedMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequest(chapter: Chapters.Cebu));

        Assert.False(result.Succeeded);
        var unchanged = await service.GetByUserIdAsync(member.UserId);
        Assert.Equal(Chapters.Ncr, unchanged!.Chapter);
    }

    [Fact]
    public async Task UpsertMyProfileAsync_AfterSubmit_UnchangedPrcLicenseNo_LeavesPrcIdVerifiedAlone()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedSubmittedMemberAsync(db, prcLicenseNo: "MP-1");
        member.PrcIdVerified = true;
        await db.SaveChangesAsync();

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequest(prcLicenseNo: "MP-1"));

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.PrcIdVerified);
    }

    [Fact]
    public async Task UpsertMyProfileAsync_AfterSubmit_ChangedPrcLicenseNo_NoReuploadFlag_ReturnsFailure()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedSubmittedMemberAsync(db, prcLicenseNo: "MP-1");

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequest(prcLicenseNo: "MP-2", prcIdReuploaded: false));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpsertMyProfileAsync_AfterSubmit_ChangedPrcLicenseNo_ReuploadFlagButNoUploadRow_ReturnsFailure()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedSubmittedMemberAsync(db, prcLicenseNo: "MP-1");

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequest(prcLicenseNo: "MP-2", prcIdReuploaded: true));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpsertMyProfileAsync_AfterSubmit_ChangedPrcLicenseNo_StaleUpload_ReturnsFailure()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var baseline = DateTimeOffset.UtcNow.AddDays(-1);
        var member = await SeedSubmittedMemberAsync(db, prcLicenseNo: "MP-1", updatedAt: baseline);
        db.MemberUploads.Add(new MemberUpload
        {
            UserId = member.UserId,
            Kind = UploadKind.PrcId,
            StorageKey = $"{member.UserId}/prc-id.pdf",
            ContentType = "application/pdf",
            CreatedAt = baseline.AddDays(-2),
        });
        await db.SaveChangesAsync();

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequest(prcLicenseNo: "MP-2", prcIdReuploaded: true));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpsertMyProfileAsync_AfterSubmit_ChangedPrcLicenseNo_FreshUpload_StagesPendingValue_WithoutTouchingCurrentOrVerified()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var baseline = DateTimeOffset.UtcNow.AddDays(-1);
        var member = await SeedSubmittedMemberAsync(db, prcLicenseNo: "MP-1", updatedAt: baseline);
        member.PrcIdVerified = true;
        db.MemberUploads.Add(new MemberUpload
        {
            UserId = member.UserId,
            Kind = UploadKind.PrcId,
            StorageKey = $"{member.UserId}/prc-id.pdf",
            ContentType = "application/pdf",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequest(prcLicenseNo: "MP-2", prcIdReuploaded: true));

        Assert.True(result.Succeeded);
        // The old value stays current, PrcIdVerified untouched - only Approve/Reject can change
        // either, per the pending-value model.
        Assert.Equal("MP-1", result.Value!.PrcLicenseNo);
        Assert.Equal("MP-2", result.Value.PendingPrcLicenseNo);
        Assert.True(result.Value.PrcIdVerified);
    }

    [Fact]
    public async Task ApprovePrcVerificationAsync_WithPendingChange_CopiesPendingIntoCurrentAndMarksVerified()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedSubmittedMemberAsync(db, prcLicenseNo: "MP-1");
        member.PendingPrcLicenseNo = "MP-2";
        await db.SaveChangesAsync();
        var adminId = Guid.NewGuid();

        var result = await service.ApprovePrcVerificationAsync(member.Id, adminId);

        Assert.True(result.Succeeded);
        var updated = await service.GetByIdAsync(member.Id);
        Assert.Equal("MP-2", updated!.PrcLicenseNo);
        Assert.Null(updated.PendingPrcLicenseNo);
        Assert.True(updated.PrcIdVerified);
        var history = Assert.Single(await db.PrcVerificationHistories.Where(h => h.MemberId == member.Id).ToListAsync());
        Assert.Equal(PrcVerificationDecision.Approved, history.Decision);
        Assert.Equal("MP-1", history.OldValue);
        Assert.Equal("MP-2", history.NewValue);
        Assert.Equal(adminId, history.DecidedByUserId);
    }

    [Fact]
    public async Task ApprovePrcVerificationAsync_NeverVerifiedWithNoPendingChange_JustMarksVerified()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedSubmittedMemberAsync(db, prcLicenseNo: "MP-1");

        var result = await service.ApprovePrcVerificationAsync(member.Id, Guid.NewGuid());

        Assert.True(result.Succeeded);
        var updated = await service.GetByIdAsync(member.Id);
        Assert.Equal("MP-1", updated!.PrcLicenseNo);
        Assert.True(updated.PrcIdVerified);
    }

    [Fact]
    public async Task RejectPrcVerificationAsync_WithPendingChange_DiscardsPendingAndSetsReason_LeavesCurrentValueAndVerifiedUnchanged()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedSubmittedMemberAsync(db, prcLicenseNo: "MP-1");
        member.PrcIdVerified = true;
        member.PendingPrcLicenseNo = "MP-2";
        await db.SaveChangesAsync();

        var result = await service.RejectPrcVerificationAsync(member.Id, "Illegible document", Guid.NewGuid());

        Assert.True(result.Succeeded);
        var updated = await service.GetByIdAsync(member.Id);
        Assert.Equal("MP-1", updated!.PrcLicenseNo);
        Assert.Null(updated.PendingPrcLicenseNo);
        Assert.Equal("Illegible document", updated.PrcVerificationRejectedReason);
        Assert.True(updated.PrcIdVerified);
    }

    [Fact]
    public async Task GetAllAsync_WithPendingPrcVerificationOnly_IncludesNeverVerifiedAndPendingChange_ExcludesVerified()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var neverVerified = await SeedSubmittedMemberAsync(db, prcLicenseNo: "MP-1");
        var pendingChange = await SeedSubmittedMemberAsync(db, prcLicenseNo: "MP-2");
        pendingChange.PrcIdVerified = true;
        pendingChange.PendingPrcLicenseNo = "MP-3";
        var verified = await SeedSubmittedMemberAsync(db, prcLicenseNo: "MP-4");
        verified.PrcIdVerified = true;
        await db.SaveChangesAsync();

        var result = await service.GetAllAsync(1, 100, "lastName", "asc", status: null, pendingPrcVerificationOnly: true);

        Assert.Contains(result.Items, m => m.Id == neverVerified.Id);
        Assert.Contains(result.Items, m => m.Id == pendingChange.Id);
        Assert.DoesNotContain(result.Items, m => m.Id == verified.Id);
    }

    [Fact]
    public async Task GetAllAsync_WithExcludeUserIds_ExcludesMatchingRowsFromItemsAndTotalCount()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var kept = await SeedSubmittedMemberAsync(db);
        var excluded = await SeedSubmittedMemberAsync(db);

        var result = await service.GetAllAsync(1, 100, "lastName", "asc", status: null, excludeUserIds: [excluded.UserId]);

        Assert.Contains(result.Items, m => m.Id == kept.Id);
        Assert.DoesNotContain(result.Items, m => m.Id == excluded.Id);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task RejectPrcVerificationAsync_ForNeverVerifiedMember_KeepsThemInTheQueue()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedSubmittedMemberAsync(db, prcLicenseNo: "MP-1");

        await service.RejectPrcVerificationAsync(member.Id, "Please resubmit", Guid.NewGuid());

        var result = await service.GetAllAsync(1, 100, "lastName", "asc", status: null, pendingPrcVerificationOnly: true);
        Assert.Contains(result.Items, m => m.Id == member.Id);
    }

    [Fact]
    public async Task UpsertMyProfileAsync_DuringDraft_ChangedPrcLicenseNo_NoReuploadNeeded()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db, prcLicenseNo: "MP-1");

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequest(prcLicenseNo: "MP-2", prcIdReuploaded: false));

        Assert.True(result.Succeeded);
        Assert.Equal("MP-2", result.Value!.PrcLicenseNo);
    }
}
