using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Models;
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
        string? prcLicenseNo = null, bool prcIdReuploaded = false,
        string firstName = "Juan", string lastName = "Dela Cruz", string? address = "123 Main St",
        string? company = null, string? website = null) => new(
        FirstName: firstName, MiddleName: null, LastName: lastName, Suffix: null,
        Birthdate: new DateOnly(1990, 1, 1), Gender: "Male", CivilStatus: "Single",
        Address: address, MobileNumber: "09171234567",
        HousePhone: null, Website: website, FacebookUrl: null, LinkedInUrl: null, XUrl: null, InstagramUrl: null,
        PrcLicenseNo: prcLicenseNo, PtrNumber: "PTR-0012345", Tin: null,
        Chapter: chapter,
        EmploymentStatus: null, Company: company, Position: null, BusinessAddress: null, YearsOfPractice: null, Specialization: null, Skills: null,
        MemberType: memberType,
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
    public async Task UpsertMyProfileAsync_RoundTripsNewPersonalInformationFields()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(
            member.UserId,
            BuildRequestWithContactDetails("09171234567", "123-456-789-000"));

        Assert.True(result.Succeeded);
        Assert.Equal("Single", result.Value!.CivilStatus);
        Assert.Equal("123 Main St", result.Value.Address);
        Assert.Equal("09171234567", result.Value.MobileNumber);
        Assert.Equal("PTR-0012345", result.Value.PtrNumber);
        Assert.Equal("123-456-789-000", result.Value.Tin);
    }

    private static UpdateMyProfileRequest BuildRequestWithContactDetails(string? mobileNumber, string? tin) => new(
        FirstName: "Juan", MiddleName: null, LastName: "Dela Cruz", Suffix: null,
        Birthdate: new DateOnly(1990, 1, 1), Gender: "Male", CivilStatus: "Single",
        Address: "123 Main St", MobileNumber: mobileNumber,
        HousePhone: null, Website: null, FacebookUrl: null, LinkedInUrl: null, XUrl: null, InstagramUrl: null,
        PrcLicenseNo: null, PtrNumber: "PTR-0012345", Tin: tin,
        Chapter: Chapters.Ncr,
        EmploymentStatus: null, Company: null, Position: null, BusinessAddress: null, YearsOfPractice: null, Specialization: null, Skills: null,
        MemberType: MemberTypes.Regular);

    [Theory]
    [InlineData("09171234567")]
    [InlineData("+639171234567")]
    [InlineData("")]
    public async Task UpsertMyProfileAsync_WithValidOrEmptyMobileNumber_Succeeds(string mobileNumber)
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequestWithContactDetails(mobileNumber, null));

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("639171234567")]
    [InlineData("0917-123-4567")]
    public async Task UpsertMyProfileAsync_WithInvalidMobileNumberFormat_ReturnsFailure(string mobileNumber)
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequestWithContactDetails(mobileNumber, null));

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("123-456-789-000")]
    [InlineData("")]
    public async Task UpsertMyProfileAsync_WithValidOrEmptyTin_Succeeds(string tin)
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequestWithContactDetails(null, tin));

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("1234567890123")]
    [InlineData("12A-456-789")]
    public async Task UpsertMyProfileAsync_WithInvalidTinFormat_ReturnsFailure(string tin)
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequestWithContactDetails(null, tin));

        Assert.False(result.Succeeded);
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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectPrcVerificationAsync_WithEmptyOrWhitespaceReason_FailsAndRecordsNoHistory(string reason)
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedSubmittedMemberAsync(db, prcLicenseNo: "MP-1");
        member.PrcIdVerified = true;
        member.PendingPrcLicenseNo = "MP-2";
        await db.SaveChangesAsync();

        var result = await service.RejectPrcVerificationAsync(member.Id, reason, Guid.NewGuid());

        Assert.False(result.Succeeded);
        var updated = await service.GetByIdAsync(member.Id);
        Assert.Equal("MP-2", updated!.PendingPrcLicenseNo);
        Assert.Null(updated.PrcVerificationRejectedReason);
        Assert.Empty(db.PrcVerificationHistories);
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

    private static UpdateMyProfileRequest BuildRequestWithContactFields(
        string? housePhone = null, string? website = null, string? facebookUrl = null,
        string? linkedInUrl = null, string? xUrl = null, string? instagramUrl = null) => new(
        FirstName: "Juan", MiddleName: null, LastName: "Dela Cruz", Suffix: null,
        Birthdate: new DateOnly(1990, 1, 1), Gender: "Male", CivilStatus: "Single",
        Address: "123 Main St", MobileNumber: "09171234567",
        HousePhone: housePhone, Website: website, FacebookUrl: facebookUrl, LinkedInUrl: linkedInUrl, XUrl: xUrl, InstagramUrl: instagramUrl,
        PrcLicenseNo: null, PtrNumber: "PTR-0012345", Tin: null,
        Chapter: Chapters.Ncr,
        EmploymentStatus: null, Company: null, Position: null, BusinessAddress: null, YearsOfPractice: null, Specialization: null, Skills: null,
        MemberType: MemberTypes.Regular);

    [Theory]
    [InlineData("(02) 8123 4567")]
    [InlineData("032-2551234")]
    [InlineData("")]
    public async Task UpsertMyProfileAsync_WithValidOrEmptyHousePhone_Succeeds(string housePhone)
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequestWithContactFields(housePhone: housePhone));

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("not-a-phone-number-at-all")]
    public async Task UpsertMyProfileAsync_WithInvalidHousePhoneFormat_ReturnsFailure(string housePhone)
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequestWithContactFields(housePhone: housePhone));

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://facebook.com/someone")]
    [InlineData("")]
    public async Task UpsertMyProfileAsync_WithValidOrEmptyWebsite_Succeeds(string website)
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequestWithContactFields(website: website));

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    public async Task UpsertMyProfileAsync_WithInvalidWebsiteFormat_ReturnsFailure(string website)
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequestWithContactFields(website: website));

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://facebook.com/someone")]
    public async Task UpsertMyProfileAsync_WithInvalidSocialUrlFormat_ReturnsFailure(string url)
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequestWithContactFields(facebookUrl: url));

        Assert.False(result.Succeeded);
    }

    private static UpdateMyProfileRequest BuildRequestWithYearsOfPractice(int? yearsOfPractice) => new(
        FirstName: "Juan", MiddleName: null, LastName: "Dela Cruz", Suffix: null,
        Birthdate: new DateOnly(1990, 1, 1), Gender: "Male", CivilStatus: "Single",
        Address: "123 Main St", MobileNumber: "09171234567",
        HousePhone: null, Website: null, FacebookUrl: null, LinkedInUrl: null, XUrl: null, InstagramUrl: null,
        PrcLicenseNo: null, PtrNumber: "PTR-0012345", Tin: null,
        Chapter: Chapters.Ncr,
        EmploymentStatus: null, Company: null, Position: null, BusinessAddress: null, YearsOfPractice: yearsOfPractice, Specialization: null, Skills: null,
        MemberType: MemberTypes.Regular);

    [Fact]
    public async Task UpsertMyProfileAsync_WithNegativeYearsOfPractice_ReturnsFailure()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequestWithYearsOfPractice(-1));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpsertMyProfileAsync_WithZeroYearsOfPractice_Succeeds()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequestWithYearsOfPractice(0));

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Value!.YearsOfPractice);
    }

    private static async Task<Member> SeedCompleteDraftAsync(TestDbContext db)
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
            PrcLicenseNo = "MP 99999",
            PtrNumber = "PTR-0099999",
            Gender = "Male",
            CivilStatus = "Single",
            Address = "123 Sample St",
            MobileNumber = "09171234567",
            Birthdate = new DateOnly(1990, 1, 1),
            Status = MembershipStatus.Pending,
            SubmittedAt = null,
        };
        db.Members.Add(member);
        await db.SaveChangesAsync();
        return member;
    }

    [Fact]
    public async Task SubmitMyProfileAsync_MissingPrcIdUpload_ReturnsFailure()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedCompleteDraftAsync(db);

        var result = await service.SubmitMyProfileAsync(member.UserId);

        Assert.False(result.Succeeded);
        var unchanged = await service.GetByUserIdAsync(member.UserId);
        Assert.Null(unchanged!.SubmittedAt);
    }

    [Fact]
    public async Task SubmitMyProfileAsync_WithPrcIdUploaded_Succeeds()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedCompleteDraftAsync(db);
        db.MemberUploads.Add(new MemberUpload
        {
            UserId = member.UserId,
            Kind = UploadKind.PrcId,
            StorageKey = $"{member.UserId}/prc-id.pdf",
            ContentType = "application/pdf",
        });
        await db.SaveChangesAsync();

        var result = await service.SubmitMyProfileAsync(member.UserId);

        Assert.True(result.Succeeded);
        var updated = await service.GetByUserIdAsync(member.UserId);
        Assert.NotNull(updated!.SubmittedAt);
    }

    [Fact]
    public async Task GetProfileCompletenessAsync_UnsubmittedDraft_ReturnsZeroPercent()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var completeness = await service.GetProfileCompletenessAsync(member.UserId);

        Assert.NotNull(completeness);
        Assert.False(completeness!.IsSubmitted);
        Assert.Equal(0, completeness.PercentComplete);
    }

    [Fact]
    public async Task GetProfileCompletenessAsync_SubmittedWithNothingElse_ReturnsBaselineFiftyPercent()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedSubmittedMemberAsync(db, prcLicenseNo: "MP-1");

        var completeness = await service.GetProfileCompletenessAsync(member.UserId);

        Assert.NotNull(completeness);
        Assert.True(completeness!.IsSubmitted);
        Assert.Equal(50, completeness.PercentComplete);
        Assert.False(completeness.HasProfessionalInfo);
        Assert.Equal(0, completeness.CertificateCount);
    }

    [Fact]
    public async Task GetProfileCompletenessAsync_SubmittedWithAllOptionalSignals_ReturnsOneHundredPercent()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedSubmittedMemberAsync(db, prcLicenseNo: "MP-1");
        member.EmploymentStatus = "Employed";
        db.MemberUploads.AddRange(
            new MemberUpload { UserId = member.UserId, Kind = UploadKind.ValidGovernmentId, StorageKey = "k1", ContentType = "image/jpeg" },
            new MemberUpload { UserId = member.UserId, Kind = UploadKind.FormalPhoto, StorageKey = "k2", ContentType = "image/jpeg" },
            new MemberUpload { UserId = member.UserId, Kind = UploadKind.Signature, StorageKey = "k3", ContentType = "image/jpeg" });
        db.MemberCertificates.Add(new MemberCertificate
        {
            UserId = member.UserId, FileName = "cert.pdf", StorageKey = "k4", ContentType = "application/pdf", FileSizeBytes = 100,
        });
        await db.SaveChangesAsync();

        var completeness = await service.GetProfileCompletenessAsync(member.UserId);

        Assert.NotNull(completeness);
        Assert.Equal(100, completeness!.PercentComplete);
        Assert.True(completeness.HasProfessionalInfo);
        Assert.True(completeness.HasValidGovernmentId);
        Assert.True(completeness.HasFormalPhoto);
        Assert.True(completeness.HasSignature);
        Assert.Equal(1, completeness.CertificateCount);
    }

    [Fact]
    public async Task GetProfileCompletenessAsync_UnknownUser_ReturnsNull()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);

        var completeness = await service.GetProfileCompletenessAsync(Guid.NewGuid());

        Assert.Null(completeness);
    }

    [Fact]
    public async Task UpsertMyProfileAsync_OverlongFirstName_ReturnsFailure_WithoutPersisting()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequest(firstName: new string('A', 129)));

        Assert.False(result.Succeeded);
        var updated = await service.GetByIdAsync(member.Id);
        Assert.Equal("Juan", updated!.FirstName);
    }

    [Fact]
    public async Task UpsertMyProfileAsync_OverlongAddress_ReturnsFailure()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequest(address: new string('A', 513)));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpsertMyProfileAsync_OverlongCompany_ReturnsFailure()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequest(company: new string('A', 257)));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpsertMyProfileAsync_OverlongWebsite_ReturnsFailure()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequest(website: "https://example.com/" + new string('a', 250)));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpsertMyProfileAsync_EmptyFirstName_StillSucceeds_DraftAutosaveTolerance()
    {
        // Requiredness is enforced at SubmitMyProfileAsync, not here - an in-progress wizard draft
        // must be able to autosave partially-filled steps.
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequest(firstName: ""));

        Assert.True(result.Succeeded);
    }

    private static async Task<Guid> SeedApplicationUserAsync(TestDbContext db)
    {
        var user = new ApplicationUser { UserName = $"{Guid.NewGuid()}@example.com", Email = $"{Guid.NewGuid()}@example.com" };
        db.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static CreateMemberRequest BuildCreateRequest(Guid userId, string firstName = "Juan", string? address = "123 Main St") => new(
        UserId: userId, MembershipNo: "000099", FirstName: firstName, MiddleName: null, LastName: "Dela Cruz", Suffix: null,
        Birthdate: new DateOnly(1990, 1, 1), Gender: "Male", CivilStatus: "Single",
        Address: address, MobileNumber: "09171234567",
        HousePhone: null, Website: null, FacebookUrl: null, LinkedInUrl: null, XUrl: null, InstagramUrl: null,
        PrcLicenseNo: null, PtrNumber: null, Tin: null,
        Chapter: Chapters.Ncr,
        EmploymentStatus: null, Company: null, Position: null, BusinessAddress: null, YearsOfPractice: null, Specialization: null, Skills: null,
        MemberType: MemberTypes.Regular, RenewalDueDate: null, NationalDuesReferenceNo: null);

    [Fact]
    public async Task CreateAsync_OverlongFirstName_ReturnsFailure_WithoutPersisting()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var userId = await SeedApplicationUserAsync(db);

        var result = await service.CreateAsync(BuildCreateRequest(userId, firstName: new string('A', 129)));

        Assert.False(result.Succeeded);
        Assert.Empty(db.Members);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_Succeeds()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var userId = await SeedApplicationUserAsync(db);

        var result = await service.CreateAsync(BuildCreateRequest(userId));

        Assert.True(result.Succeeded);
        Assert.Equal("Juan", result.Value!.FirstName);
    }

    private static UpdateMemberRequest BuildUpdateRequest(string? address = "123 Main St") => new(
        FirstName: "Juan", MiddleName: null, LastName: "Dela Cruz", Suffix: null,
        Birthdate: new DateOnly(1990, 1, 1), Gender: "Male", CivilStatus: "Single",
        Address: address, MobileNumber: "09171234567",
        HousePhone: null, Website: null, FacebookUrl: null, LinkedInUrl: null, XUrl: null, InstagramUrl: null,
        PrcLicenseNo: null, PtrNumber: null, Tin: null,
        Chapter: Chapters.Ncr,
        EmploymentStatus: null, Company: null, Position: null, BusinessAddress: null, YearsOfPractice: null, Specialization: null, Skills: null,
        MemberType: MemberTypes.Regular, Status: MembershipStatus.Pending, RenewalDueDate: null, NationalDuesReferenceNo: null);

    [Fact]
    public async Task UpdateAsync_OverlongAddress_ReturnsFailure_WithoutPersisting()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpdateAsync(member.Id, BuildUpdateRequest(address: new string('A', 513)));

        Assert.False(result.Succeeded);
        var updated = await service.GetByIdAsync(member.Id);
        Assert.Null(updated!.Address);
    }

    [Fact]
    public async Task DeleteAsync_MemberWithNoPrcVerificationHistory_Succeeds()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedSubmittedMemberAsync(db, prcLicenseNo: "MP-1");

        var result = await service.DeleteAsync(member.Id);

        Assert.True(result.Succeeded);
        Assert.Null(await service.GetByIdAsync(member.Id));
    }

    [Fact]
    public async Task DeleteAsync_MemberWithPrcVerificationHistory_FailsAndLeavesMemberIntact()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedSubmittedMemberAsync(db, prcLicenseNo: "MP-1");
        await service.ApprovePrcVerificationAsync(member.Id, Guid.NewGuid());

        var result = await service.DeleteAsync(member.Id);

        Assert.False(result.Succeeded);
        Assert.NotNull(await service.GetByIdAsync(member.Id));
    }
}
