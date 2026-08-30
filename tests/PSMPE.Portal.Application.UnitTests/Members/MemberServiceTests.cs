using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Configuration;
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
        string? prcLicenseNo = null, DateOnly? prcRegistrationDate = null, DateOnly? prcValidUntilDate = null, bool prcIdReuploaded = false,
        string firstName = "Juan", string lastName = "Dela Cruz", string? address = "123 Main St",
        string? company = null,
        int? chapterYear = null, string? chapterPosition = null,
        string? ptrPlaceIssued = null, DateOnly? ptrDateIssued = null) => new(
        FirstName: firstName, MiddleName: null, LastName: lastName, Suffix: null,
        Birthdate: new DateOnly(1990, 1, 1), Gender: "Male", CivilStatus: "Single",
        EducationLevel: null, SchoolName: null, CourseYearGraduated: null, SpecifiedProfession: null,
        MobileNumber: "09171234567",
        HouseNo: null, Street: address, Barangay: null, CityMunicipality: null, Province: null, ZipCode: null, Country: null,
        MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null, MailingCountry: null,
        HousePhone: null,
        PrcLicenseNo: prcLicenseNo, PrcRegistrationDate: prcRegistrationDate, PrcValidUntilDate: prcValidUntilDate, PtrNumber: "PTR-0012345", PtrPlaceIssued: ptrPlaceIssued, PtrDateIssued: ptrDateIssued, Tin: null,
        Chapter: chapter, ChapterYear: chapterYear, ChapterPosition: chapterPosition,
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
        Assert.Equal("123 Main St", result.Value.Street);
        Assert.Equal("09171234567", result.Value.MobileNumber);
        Assert.Equal("PTR-0012345", result.Value.PtrNumber);
        Assert.Equal("123-456-789-000", result.Value.Tin);
    }

    private static UpdateMyProfileRequest BuildRequestWithContactDetails(string? mobileNumber, string? tin) => new(
        FirstName: "Juan", MiddleName: null, LastName: "Dela Cruz", Suffix: null,
        Birthdate: new DateOnly(1990, 1, 1), Gender: "Male", CivilStatus: "Single",
        EducationLevel: null, SchoolName: null, CourseYearGraduated: null, SpecifiedProfession: null,
        MobileNumber: mobileNumber,
        HouseNo: null, Street: "123 Main St", Barangay: null, CityMunicipality: null, Province: null, ZipCode: null, Country: null,
        MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null, MailingCountry: null,
        HousePhone: null,
        PrcLicenseNo: null, PrcRegistrationDate: null, PrcValidUntilDate: null, PtrNumber: "PTR-0012345", PtrPlaceIssued: null, PtrDateIssued: null, Tin: tin,
        Chapter: Chapters.Ncr, ChapterYear: null, ChapterPosition: null,
        EmploymentStatus: null, Company: null, Position: null, BusinessAddress: null, YearsOfPractice: null, Specialization: null, Skills: null,
        MemberType: MemberTypes.Regular);

    [Theory]
    [InlineData("09171234567")]
    [InlineData("+639171234567")]
    [InlineData("639171234567")]
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
    [InlineData("8171234567")]
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

    /// <summary>
    /// Backs the "Applied" column on the consolidated Members page's Pending Approval tab, which
    /// defaults to oldest-first - the natural reading order for a work queue.
    /// </summary>
    [Fact]
    public async Task GetAllAsync_SortedBySubmittedAt_OrdersOldestApplicationFirst()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var newest = await SeedSubmittedMemberAsync(db);
        var oldest = await SeedSubmittedMemberAsync(db);
        newest.SubmittedAt = DateTimeOffset.UtcNow.AddDays(-1);
        oldest.SubmittedAt = DateTimeOffset.UtcNow.AddDays(-30);
        await db.SaveChangesAsync();

        var ascending = await service.GetAllAsync(1, 100, "submittedAt", "asc", status: null);
        Assert.Equal(oldest.Id, ascending.Items[0].Id);

        var descending = await service.GetAllAsync(1, 100, "submittedAt", "desc", status: null);
        Assert.Equal(newest.Id, descending.Items[0].Id);
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
    public async Task GetAllAsync_WithSearch_MatchesNameMembershipNoOrEmail_CaseInsensitively()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var match = new Member
        {
            UserId = Guid.NewGuid(),
            User = new ApplicationUser { UserName = "maria.santos@example.com", Email = "maria.santos@example.com" },
            MembershipNo = "000042",
            FirstName = "Maria",
            LastName = "Santos",
            Chapter = Chapters.Ncr,
            MemberType = MemberTypes.Regular,
            Status = MembershipStatus.Active,
            SubmittedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        var nonMatch = new Member
        {
            UserId = Guid.NewGuid(),
            User = new ApplicationUser { UserName = "pedro.reyes@example.com", Email = "pedro.reyes@example.com" },
            MembershipNo = "000099",
            FirstName = "Pedro",
            LastName = "Reyes",
            Chapter = Chapters.Cebu,
            MemberType = MemberTypes.Regular,
            Status = MembershipStatus.Active,
            SubmittedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        db.Members.AddRange(match, nonMatch);
        await db.SaveChangesAsync();

        var byName = await service.GetAllAsync(1, 100, "lastName", "asc", status: null, search: "SANTOS");
        Assert.Single(byName.Items);
        Assert.Equal(match.Id, byName.Items[0].Id);

        var byMembershipNo = await service.GetAllAsync(1, 100, "lastName", "asc", status: null, search: "000042");
        Assert.Single(byMembershipNo.Items);
        Assert.Equal(match.Id, byMembershipNo.Items[0].Id);

        var byEmail = await service.GetAllAsync(1, 100, "lastName", "asc", status: null, search: "maria.santos");
        Assert.Single(byEmail.Items);
        Assert.Equal(match.Id, byEmail.Items[0].Id);
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

    private static UpdateMyProfileRequest BuildRequestWithContactFields(string? housePhone = null) => new(
        FirstName: "Juan", MiddleName: null, LastName: "Dela Cruz", Suffix: null,
        Birthdate: new DateOnly(1990, 1, 1), Gender: "Male", CivilStatus: "Single",
        EducationLevel: null, SchoolName: null, CourseYearGraduated: null, SpecifiedProfession: null,
        MobileNumber: "09171234567",
        HouseNo: null, Street: "123 Main St", Barangay: null, CityMunicipality: null, Province: null, ZipCode: null, Country: null,
        MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null, MailingCountry: null,
        HousePhone: housePhone,
        PrcLicenseNo: null, PrcRegistrationDate: null, PrcValidUntilDate: null, PtrNumber: "PTR-0012345", PtrPlaceIssued: null, PtrDateIssued: null, Tin: null,
        Chapter: Chapters.Ncr, ChapterYear: null, ChapterPosition: null,
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




    private static UpdateMyProfileRequest BuildRequestWithYearsOfPractice(int? yearsOfPractice) => new(
        FirstName: "Juan", MiddleName: null, LastName: "Dela Cruz", Suffix: null,
        Birthdate: new DateOnly(1990, 1, 1), Gender: "Male", CivilStatus: "Single",
        EducationLevel: null, SchoolName: null, CourseYearGraduated: null, SpecifiedProfession: null,
        MobileNumber: "09171234567",
        HouseNo: null, Street: "123 Main St", Barangay: null, CityMunicipality: null, Province: null, ZipCode: null, Country: null,
        MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null, MailingCountry: null,
        HousePhone: null,
        PrcLicenseNo: null, PrcRegistrationDate: null, PrcValidUntilDate: null, PtrNumber: "PTR-0012345", PtrPlaceIssued: null, PtrDateIssued: null, Tin: null,
        Chapter: Chapters.Ncr, ChapterYear: null, ChapterPosition: null,
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

    [Theory]
    [InlineData(1899)]
    [InlineData(2201)]
    public async Task UpsertMyProfileAsync_WithChapterYearOutOfRange_ReturnsFailure(int chapterYear)
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequest(chapterYear: chapterYear));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpsertMyProfileAsync_WithChapterOfficerAndPtrIssuance_RoundTrips()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequest(
            chapterYear: 2024, chapterPosition: "Secretary",
            ptrPlaceIssued: "Quezon City", ptrDateIssued: new DateOnly(2024, 1, 15)));

        Assert.True(result.Succeeded);
        Assert.Equal(2024, result.Value!.ChapterYear);
        Assert.Equal("Secretary", result.Value.ChapterPosition);
        Assert.Equal("Quezon City", result.Value.PtrPlaceIssued);
        Assert.Equal(new DateOnly(2024, 1, 15), result.Value.PtrDateIssued);
    }

    [Fact]
    public async Task UpsertMyProfileAsync_WithOverlongChapterPosition_ReturnsFailure()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequest(chapterPosition: new string('a', 129)));

        Assert.False(result.Succeeded);
    }

    /// <summary>
    /// Chapter and MemberType are locked once an application is submitted; the officer post
    /// deliberately is not, since it describes a role the member holds rather than their
    /// eligibility. This guards that distinction against a future tightening of the lock.
    /// </summary>
    [Fact]
    public async Task UpsertMyProfileAsync_ChapterOfficerEditableAfterSubmission()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);
        member.SubmittedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequest(
            chapter: member.Chapter, memberType: member.MemberType,
            chapterYear: 2025, chapterPosition: "Treasurer"));

        Assert.True(result.Succeeded);
        Assert.Equal(2025, result.Value!.ChapterYear);
        Assert.Equal("Treasurer", result.Value.ChapterPosition);
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
            PrcRegistrationDate = new DateOnly(2020, 1, 1),
            PrcValidUntilDate = new DateOnly(2030, 1, 1),
            PtrNumber = "PTR-0099999",
            Gender = "Male",
            CivilStatus = "Single",
            EducationLevel = "College / University",
            SchoolName = "Sample University",
            CourseYearGraduated = "BSCE 2015",
            SpecifiedProfession = "Master Plumber",
            Street = "123 Sample St",
            Barangay = "Sample Barangay",
            CityMunicipality = "Sample City",
            Province = "Sample Province",
            ZipCode = "1000",
            Country = "Philippines",
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
        db.MemberUploads.AddRange(
            new MemberUpload
            {
                UserId = member.UserId,
                Kind = UploadKind.PrcId,
                StorageKey = $"{member.UserId}/prc-id.pdf",
                ContentType = "application/pdf",
            },
            new MemberUpload
            {
                UserId = member.UserId,
                Kind = UploadKind.Photo,
                StorageKey = $"{member.UserId}/photo.jpg",
                ContentType = "image/jpeg",
            },
            new MemberUpload
            {
                UserId = member.UserId,
                Kind = UploadKind.ProofOfPayment,
                StorageKey = $"{member.UserId}/proof-of-payment.jpg",
                ContentType = "image/jpeg",
            });
        await db.SaveChangesAsync();

        var result = await service.SubmitMyProfileAsync(member.UserId);

        Assert.True(result.Succeeded);
        var updated = await service.GetByUserIdAsync(member.UserId);
        Assert.NotNull(updated!.SubmittedAt);
    }

    /// <summary>
    /// Registration path: ticking the portal opt-in adds the resolved PortalFee on top of
    /// MembershipFee+ShippingFee, and stamps the created Payment accordingly - no fee rows seeded,
    /// so this exercises MembershipFeeKeys' shipped defaults (1500 + 200 + 900 = 2600).
    /// </summary>
    [Fact]
    public async Task SubmitMyProfileAsync_WithIncludePortalAccessTrue_AddsPortalFeeToTheRegistrationPayment()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedCompleteDraftAsync(db);
        db.MemberUploads.AddRange(
            new MemberUpload { UserId = member.UserId, Kind = UploadKind.PrcId, StorageKey = $"{member.UserId}/prc-id.pdf", ContentType = "application/pdf" },
            new MemberUpload { UserId = member.UserId, Kind = UploadKind.Photo, StorageKey = $"{member.UserId}/photo.jpg", ContentType = "image/jpeg" },
            new MemberUpload { UserId = member.UserId, Kind = UploadKind.ProofOfPayment, StorageKey = $"{member.UserId}/proof-of-payment.jpg", ContentType = "image/jpeg" });
        await db.SaveChangesAsync();

        var result = await service.SubmitMyProfileAsync(member.UserId, includePortalAccess: true);

        Assert.True(result.Succeeded);
        var payment = await db.Payments.SingleAsync(p => p.MemberId == member.Id && p.Kind == PaymentKind.NewMembership);
        Assert.True(payment.IncludesPortalAccess);
        Assert.Equal(
            MembershipFeeKeys.DefaultMembershipFee + MembershipFeeKeys.DefaultShippingFee + MembershipFeeKeys.DefaultPortalFee,
            payment.Amount);
    }

    [Fact]
    public async Task SubmitMyProfileAsync_WithoutIncludePortalAccess_LeavesPortalFeeOutOfTheRegistrationPayment()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedCompleteDraftAsync(db);
        db.MemberUploads.AddRange(
            new MemberUpload { UserId = member.UserId, Kind = UploadKind.PrcId, StorageKey = $"{member.UserId}/prc-id.pdf", ContentType = "application/pdf" },
            new MemberUpload { UserId = member.UserId, Kind = UploadKind.Photo, StorageKey = $"{member.UserId}/photo.jpg", ContentType = "image/jpeg" },
            new MemberUpload { UserId = member.UserId, Kind = UploadKind.ProofOfPayment, StorageKey = $"{member.UserId}/proof-of-payment.jpg", ContentType = "image/jpeg" });
        await db.SaveChangesAsync();

        var result = await service.SubmitMyProfileAsync(member.UserId);

        Assert.True(result.Succeeded);
        var payment = await db.Payments.SingleAsync(p => p.MemberId == member.Id && p.Kind == PaymentKind.NewMembership);
        Assert.False(payment.IncludesPortalAccess);
        Assert.Equal(MembershipFeeKeys.DefaultMembershipFee + MembershipFeeKeys.DefaultShippingFee, payment.Amount);
    }

    /// <summary>Admin walk-in path: RecordPaymentRequest.IncludePortalAccess flows onto the Payment
    /// ResolveRegistrationPaymentAsync creates, and PaymentVerification.Apply (run by ApproveAsync
    /// in the same transaction) grants Member.HasPortalAccess from it.</summary>
    [Fact]
    public async Task ApproveAsync_WithIncludePortalAccessTrue_SetsIncludesPortalAccessOnTheCreatedPayment()
    {
        using var db = TestDbContext.CreateInMemory();
        var member = await SeedSubmittedMemberAsync(db);
        member.PrcIdVerified = true;
        await db.SaveChangesAsync();
        var service = new MemberService(db);
        var payment = new RecordPaymentRequest(
            2600m, "REF-001", DateOnly.FromDateTime(DateTime.UtcNow), "uploads/proof.jpg", IncludePortalAccess: true);

        var result = await service.ApproveAsync(member.Id, new ApproveMemberRequest("000123", payment), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(member.HasPortalAccess);
        var created = await db.Payments.SingleAsync(p => p.MemberId == member.Id);
        Assert.True(created.IncludesPortalAccess);
    }

    [Fact]
    public async Task ApproveAsync_WithoutIncludePortalAccess_LeavesPortalAccessFalseOnMemberAndPayment()
    {
        using var db = TestDbContext.CreateInMemory();
        var member = await SeedSubmittedMemberAsync(db);
        member.PrcIdVerified = true;
        await db.SaveChangesAsync();
        var service = new MemberService(db);
        var payment = new RecordPaymentRequest(500m, "REF-001", DateOnly.FromDateTime(DateTime.UtcNow), "uploads/proof.jpg");

        var result = await service.ApproveAsync(member.Id, new ApproveMemberRequest("000123", payment), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(member.HasPortalAccess);
        var created = await db.Payments.SingleAsync(p => p.MemberId == member.Id);
        Assert.False(created.IncludesPortalAccess);
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
            new MemberUpload { UserId = member.UserId, Kind = UploadKind.Photo, StorageKey = "k2", ContentType = "image/jpeg" },
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
        Assert.True(completeness.HasPhoto);
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
    public async Task UpsertMyProfileAsync_OverlongStreet_ReturnsFailure()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpsertMyProfileAsync(member.UserId, BuildRequest(address: new string('A', 129)));

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

    private static CreateMemberRequest BuildCreateRequest(
        Guid userId, string firstName = "Juan", string? address = "123 Main St", string? prcLicenseNo = "MP-99") => new(
        UserId: userId, MembershipNo: "000099", FirstName: firstName, MiddleName: null, LastName: "Dela Cruz", Suffix: null,
        Birthdate: new DateOnly(1990, 1, 1), Gender: "Male", CivilStatus: "Single",
        EducationLevel: null, SchoolName: null, CourseYearGraduated: null, SpecifiedProfession: null,
        MobileNumber: "09171234567",
        HouseNo: null, Street: address, Barangay: null, CityMunicipality: null, Province: null, ZipCode: null, Country: null,
        MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null, MailingCountry: null,
        HousePhone: null,
        PrcLicenseNo: prcLicenseNo, PrcRegistrationDate: null, PrcValidUntilDate: null, PtrNumber: null, PtrPlaceIssued: null, PtrDateIssued: null, Tin: null,
        Chapter: Chapters.Ncr, ChapterYear: null, ChapterPosition: null,
        EmploymentStatus: null, Company: null, Position: null, BusinessAddress: null, YearsOfPractice: null, Specialization: null, Skills: null,
        MemberType: MemberTypes.Regular, RenewalDueDate: null, NationalDuesReferenceNo: null);

    /// <summary>
    /// Without a licence number a member never enters the verification queue, and approval now
    /// requires verification - so creating one would produce a permanently unapprovable record.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_WithoutAPrcLicenseNo_ReturnsFailure_WithoutPersisting(string? prcLicenseNo)
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var userId = await SeedApplicationUserAsync(db);

        var result = await service.CreateAsync(BuildCreateRequest(userId, prcLicenseNo: prcLicenseNo));

        Assert.False(result.Succeeded);
        Assert.Empty(db.Members);
    }

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
        EducationLevel: null, SchoolName: null, CourseYearGraduated: null, SpecifiedProfession: null,
        MobileNumber: "09171234567",
        HouseNo: null, Street: address, Barangay: null, CityMunicipality: null, Province: null, ZipCode: null, Country: null,
        MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null, MailingCountry: null,
        HousePhone: null,
        PrcLicenseNo: null, PrcRegistrationDate: null, PrcValidUntilDate: null, PtrNumber: null, PtrPlaceIssued: null, PtrDateIssued: null, Tin: null,
        Chapter: Chapters.Ncr, ChapterYear: null, ChapterPosition: null,
        EmploymentStatus: null, Company: null, Position: null, BusinessAddress: null, YearsOfPractice: null, Specialization: null, Skills: null,
        MemberType: MemberTypes.Regular, Status: MembershipStatus.Pending, RenewalDueDate: null, NationalDuesReferenceNo: null);

    [Fact]
    public async Task UpdateAsync_OverlongStreet_ReturnsFailure_WithoutPersisting()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedDraftMemberAsync(db);

        var result = await service.UpdateAsync(member.Id, BuildUpdateRequest(address: new string('A', 129)));

        Assert.False(result.Succeeded);
        var updated = await service.GetByIdAsync(member.Id);
        Assert.Null(updated!.Street);
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

    [Fact]
    public async Task HasPrcVerificationHistoryAsync_MemberWithHistory_ReturnsTrue()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedSubmittedMemberAsync(db, prcLicenseNo: "MP-1");
        await service.ApprovePrcVerificationAsync(member.Id, Guid.NewGuid());

        Assert.True(await service.HasPrcVerificationHistoryAsync(member.UserId));
    }

    [Fact]
    public async Task HasPrcVerificationHistoryAsync_MemberWithoutHistory_ReturnsFalse()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);
        var member = await SeedSubmittedMemberAsync(db, prcLicenseNo: "MP-1");

        Assert.False(await service.HasPrcVerificationHistoryAsync(member.UserId));
    }

    [Fact]
    public async Task HasPrcVerificationHistoryAsync_UserWithNoMemberProfile_ReturnsFalse()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new MemberService(db);

        Assert.False(await service.HasPrcVerificationHistoryAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ApproveAsync_WritesAuditLogRow()
    {
        using var db = TestDbContext.CreateInMemory();
        var member = await SeedSubmittedMemberAsync(db);
        member.PrcIdVerified = true;
        await db.SaveChangesAsync();
        var service = new MemberService(db);
        var adminId = Guid.NewGuid();
        var payment = new RecordPaymentRequest(500m, "REF-001", DateOnly.FromDateTime(DateTime.UtcNow), "uploads/proof.jpg");

        await service.ApproveAsync(member.Id, new ApproveMemberRequest("000123", payment), adminId, CancellationToken.None);

        var row = Assert.Single(db.AuditLogs);
        Assert.Equal("membership.approved", row.EventType);
        Assert.Equal(adminId, row.ActorUserId);
        Assert.Equal("Member", row.TargetType);
        Assert.Equal(member.Id, row.TargetId);
        Assert.Contains("000123", row.Metadata);
    }

    [Fact]
    public async Task ApproveAsync_ReApprovingAlreadyApprovedMember_WritesNoAdditionalAuditLogRow()
    {
        using var db = TestDbContext.CreateInMemory();
        var member = await SeedSubmittedMemberAsync(db);
        member.PrcIdVerified = true;
        await db.SaveChangesAsync();
        var service = new MemberService(db);
        var adminId = Guid.NewGuid();
        var payment = new RecordPaymentRequest(500m, "REF-001", DateOnly.FromDateTime(DateTime.UtcNow), "uploads/proof.jpg");
        await service.ApproveAsync(member.Id, new ApproveMemberRequest("000123", payment), adminId, CancellationToken.None);

        // Second call: the member now has an existing (accepted) payment, so pass Payment: null -
        // ApproveAsync's short-circuit on an already-approved member returns before that matters.
        await service.ApproveAsync(member.Id, new ApproveMemberRequest("000123", null), adminId, CancellationToken.None);

        Assert.Single(db.AuditLogs);
    }

    [Fact]
    public async Task ApproveAsync_FailedValidation_WritesNoAuditLogRow()
    {
        using var db = TestDbContext.CreateInMemory();
        var member = await SeedSubmittedMemberAsync(db); // PrcIdVerified left false
        var service = new MemberService(db);
        var payment = new RecordPaymentRequest(500m, "REF-001", DateOnly.FromDateTime(DateTime.UtcNow), "uploads/proof.jpg");

        await service.ApproveAsync(member.Id, new ApproveMemberRequest("000123", payment), Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(db.AuditLogs);
    }
}
