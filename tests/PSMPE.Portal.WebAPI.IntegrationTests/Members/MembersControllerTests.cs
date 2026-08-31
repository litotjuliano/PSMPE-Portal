using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Events;
using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Application.Members.Dtos;
using PSMPE.Portal.Application.Payments;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.WebAPI.Controllers;
using SkiaSharp;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Members;

/// <summary>
/// Exercises MembersController directly against the real MemberService/UserManager (backed by
/// the InMemory database from CustomWebApplicationFactory), bypassing the HTTP/auth pipeline -
/// same convention as AdminControllerTests. CreateController(...) sets a ControllerContext with
/// a NameIdentifier claim for the /me endpoints, which read User directly.
/// </summary>
public class MembersControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMemberService _memberService;
    private readonly IMemberUploadService _memberUploadService;
    private readonly IMemberCertificateService _memberCertificateService;
    private readonly IEmailSender _emailSender;
    private readonly IPaymentService _paymentService;
    private readonly IEventService _eventService;

    public MembersControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _scope = factory.Services.CreateScope();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _memberService = _scope.ServiceProvider.GetRequiredService<IMemberService>();
        _memberUploadService = _scope.ServiceProvider.GetRequiredService<IMemberUploadService>();
        _memberCertificateService = _scope.ServiceProvider.GetRequiredService<IMemberCertificateService>();
        _emailSender = _scope.ServiceProvider.GetRequiredService<IEmailSender>();
        _paymentService = _scope.ServiceProvider.GetRequiredService<IPaymentService>();
        _eventService = _scope.ServiceProvider.GetRequiredService<IEventService>();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        _scope.Dispose();
        return Task.CompletedTask;
    }

    private MembersController CreateController(Guid? callerId = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, (callerId ?? Guid.NewGuid()).ToString()) };
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) };
        return new MembersController(_memberService, _memberUploadService, _memberCertificateService, _userManager, _emailSender, _paymentService, _eventService)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private async Task<ApplicationUser> CreateUserAsync(string role = RoleNames.Member)
    {
        var user = new ApplicationUser
        {
            UserName = $"{Guid.NewGuid()}@example.com",
            Email = $"{Guid.NewGuid()}@example.com",
            DisplayName = "Test User"
        };
        await _userManager.CreateAsync(user, "Password123!");
        await _userManager.AddToRoleAsync(user, role);
        return user;
    }

    /// <summary>
    /// Clears RMP verification, which MemberService.ApproveAsync now requires before an application
    /// can be approved. Most approval tests are about the Membership ID rules rather than the
    /// licence check, so the step lives here instead of being spelled out in each of them.
    /// </summary>
    private static async Task VerifyRmpAsync(MembersController controller, Guid memberId)
    {
        Assert.IsType<NoContentResult>(await controller.ApprovePrcVerification(memberId, CancellationToken.None));
    }

    /// <summary>
    /// Approval now admits the member and accepts their registration payment in one transaction, so
    /// every approval needs a payment. Admin-created members (BuildCreateRequest) have none, so
    /// these tests supply one the way the approval wizard does for a walk-in.
    ///
    /// Most approval tests are about the Membership ID rules rather than the money, so the details
    /// live here instead of being restated in each of them.
    /// </summary>
    private static ApproveMemberRequest ApproveWithPayment(string membershipNo) => new(
        membershipNo,
        new RecordPaymentRequest(
            Amount: 1700m,
            ReferenceNo: "REF-TEST",
            PaidOn: DateOnly.FromDateTime(DateTime.UtcNow),
            ProofStorageKey: "test/proof.jpg"));

    private static CreateMemberRequest BuildCreateRequest(Guid userId, string? membershipNo = null) => new(
        UserId: userId,
        MembershipNo: membershipNo ?? Guid.NewGuid().ToString("N")[..8],
        FirstName: "Juan",
        MiddleName: null,
        LastName: "Dela Cruz",
        Suffix: null,
        Birthdate: new DateOnly(1985, 4, 5),
        Gender: "Male",
        CivilStatus: "Single",
        EducationLevel: "College / University",
        SchoolName: "Sample University",
        CourseYearGraduated: "BSCE 2010",
        SpecifiedProfession: "Master Plumber",
        MobileNumber: "09171234567",
        HouseNo: null,
        Street: "1234 Main St",
        Barangay: "Sample Barangay",
        CityMunicipality: "Quezon City",
        Province: "Metro Manila",
        ZipCode: "1100", Country: "Philippines",
        MailingHouseNo: null,
        MailingStreet: null,
        MailingBarangay: null,
        MailingCityMunicipality: null,
        MailingProvince: null,
        MailingZipCode: null, MailingCountry: null,
        HousePhone: null,
        PrcLicenseNo: "MP 12345",
        PrcRegistrationDate: new DateOnly(2020, 1, 1),
        PrcValidUntilDate: new DateOnly(2030, 1, 1),
        PtrNumber: "PTR-0012345", PtrPlaceIssued: null, PtrDateIssued: null,
        Tin: null,
        Chapter: Chapters.QuezonCity, ChapterYear: null, ChapterPosition: null,
        EmploymentStatus: null,
        Company: "JLA Plumbing Works Inc.",
        Position: null,
        BusinessAddress: null,
        YearsOfPractice: null,
        Specialization: null,
        Skills: null,
        MemberType: MemberTypes.Regular,
        RenewalDueDate: DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
        NationalDuesReferenceNo: "AR 0012345");

    [Fact]
    public async Task Create_LinksToExistingUser_ReturnsMemberDto()
    {
        var user = await CreateUserAsync();
        var controller = CreateController();

        var result = await controller.Create(BuildCreateRequest(user.Id), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<MemberDto>(ok.Value);
        Assert.Equal(user.Id, dto.UserId);
        Assert.Equal(user.Email, dto.Email);
        Assert.Equal(MembershipStatus.Pending, dto.Status);
    }

    [Fact]
    public async Task Create_RoundTripsNewPersonalInformationFields()
    {
        var user = await CreateUserAsync();
        var controller = CreateController();

        var result = await controller.Create(BuildCreateRequest(user.Id), CancellationToken.None);

        var dto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Single", dto.CivilStatus);
        Assert.Equal("09171234567", dto.MobileNumber);
        Assert.Equal("PTR-0012345", dto.PtrNumber);
        Assert.Null(dto.Tin);

        var updateRequest = new UpdateMemberRequest(
            FirstName: dto.FirstName, MiddleName: dto.MiddleName, LastName: dto.LastName, Suffix: dto.Suffix,
            Birthdate: dto.Birthdate, Gender: dto.Gender, CivilStatus: "Married",
            EducationLevel: dto.EducationLevel, SchoolName: dto.SchoolName, CourseYearGraduated: dto.CourseYearGraduated, SpecifiedProfession: dto.SpecifiedProfession,
            MobileNumber: "09181234567",
            HouseNo: dto.HouseNo, Street: dto.Street, Barangay: dto.Barangay, CityMunicipality: dto.CityMunicipality, Province: dto.Province, ZipCode: dto.ZipCode, Country: dto.Country,
            MailingHouseNo: dto.MailingHouseNo, MailingStreet: dto.MailingStreet, MailingBarangay: dto.MailingBarangay,
            MailingCityMunicipality: dto.MailingCityMunicipality, MailingProvince: dto.MailingProvince, MailingZipCode: dto.MailingZipCode, MailingCountry: dto.MailingCountry,
            HousePhone: null,
            PrcLicenseNo: dto.PrcLicenseNo, PrcRegistrationDate: dto.PrcRegistrationDate, PrcValidUntilDate: dto.PrcValidUntilDate,
            PtrNumber: "PTR-9999999", PtrPlaceIssued: null, PtrDateIssued: null, Tin: "123-456-789",
            Chapter: dto.Chapter, ChapterYear: dto.ChapterYear, ChapterPosition: dto.ChapterPosition, EmploymentStatus: null, Company: dto.Company, Position: null, BusinessAddress: null,
            YearsOfPractice: null, Specialization: null, Skills: null, MemberType: dto.MemberType, Status: dto.Status,
            RenewalDueDate: dto.RenewalDueDate, NationalDuesReferenceNo: dto.NationalDuesReferenceNo);
        await controller.Update(dto.Id, updateRequest, CancellationToken.None);

        var updated = await controller.GetById(dto.Id, CancellationToken.None);
        var updatedDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(updated.Result).Value);
        Assert.Equal("Married", updatedDto.CivilStatus);
        Assert.Equal("09181234567", updatedDto.MobileNumber);
        Assert.Equal("PTR-9999999", updatedDto.PtrNumber);
        Assert.Equal("123-456-789", updatedDto.Tin);
    }

    [Fact]
    public async Task Create_UnknownUserId_ReturnsBadRequest()
    {
        var controller = CreateController();

        var result = await controller.Create(BuildCreateRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_UserAlreadyHasProfile_ReturnsConflict()
    {
        var user = await CreateUserAsync();
        var controller = CreateController();
        await controller.Create(BuildCreateRequest(user.Id), CancellationToken.None);

        var result = await controller.Create(BuildCreateRequest(user.Id), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_DuplicateMembershipNo_ReturnsConflict()
    {
        var userA = await CreateUserAsync();
        var userB = await CreateUserAsync();
        var controller = CreateController();
        var sharedNo = Guid.NewGuid().ToString("N")[..8];
        await controller.Create(BuildCreateRequest(userA.Id, sharedNo), CancellationToken.None);

        var result = await controller.Create(BuildCreateRequest(userB.Id, sharedNo), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_ReturnsCreatedMember()
    {
        var user = await CreateUserAsync();
        var controller = CreateController();
        await controller.Create(BuildCreateRequest(user.Id), CancellationToken.None);

        var result = await controller.GetAll(page: 1, pageSize: 1000, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var paged = Assert.IsType<PagedResult<MemberDto>>(ok.Value);
        Assert.Contains(paged.Items, m => m.UserId == user.Id);
    }

    [Fact]
    public async Task GetById_ReturnsMember()
    {
        var user = await CreateUserAsync();
        var controller = CreateController();
        var created = await controller.Create(BuildCreateRequest(user.Id), CancellationToken.None);
        var createdDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(created.Result).Value);

        var result = await controller.GetById(createdDto.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<MemberDto>(ok.Value);
        Assert.Equal(user.Id, dto.UserId);
    }

    [Fact]
    public async Task GetById_UnknownId_ReturnsNotFound()
    {
        var controller = CreateController();

        var result = await controller.GetById(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetMyProfile_ReturnsNotFound_BeforeProfileExists()
    {
        var user = await CreateUserAsync();
        var controller = CreateController(user.Id);

        var result = await controller.GetMyProfile(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task UpdateMyProfile_CreatesProfileOnFirstSave_ThenGetMyProfileReturnsIt()
    {
        var user = await CreateUserAsync();
        var controller = CreateController(user.Id);
        var request = new UpdateMyProfileRequest(
            FirstName: "Maria", MiddleName: null, LastName: "Santos", Suffix: null,
            Birthdate: null, Gender: "Female", CivilStatus: null,
            EducationLevel: null, SchoolName: null, CourseYearGraduated: null, SpecifiedProfession: null,
            MobileNumber: null,
            HouseNo: null, Street: "Cebu City", Barangay: null, CityMunicipality: null, Province: null, ZipCode: null, Country: null,
            MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null, MailingCountry: null,
            HousePhone: null,
            PrcLicenseNo: null, PrcRegistrationDate: null, PrcValidUntilDate: null, PtrNumber: null, PtrPlaceIssued: null, PtrDateIssued: null, Tin: null, Chapter: Chapters.Cebu, ChapterYear: null, ChapterPosition: null,
            EmploymentStatus: null, Company: null, Position: null, BusinessAddress: null, YearsOfPractice: null, Specialization: null, Skills: null,
            MemberType: MemberTypes.Regular);

        var updateResult = await controller.UpdateMyProfile(request, CancellationToken.None);
        var updated = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(updateResult.Result).Value);
        Assert.Equal(MembershipStatus.Pending, updated.Status);
        // The portal no longer invents a number - PSMPE assigns its own control number at approval.
        Assert.Null(updated.MembershipNo);

        var getResult = await controller.GetMyProfile(CancellationToken.None);
        var fetched = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(getResult.Result).Value);
        Assert.Equal("Maria", fetched.FirstName);
        Assert.Equal(updated.MembershipNo, fetched.MembershipNo);
    }

    [Fact]
    public async Task UpdateMyProfile_DoesNotLetCallerSetStatusOrMembershipNo()
    {
        var user = await CreateUserAsync();
        var controller = CreateController(user.Id);
        var request = new UpdateMyProfileRequest(
            FirstName: "Ana", MiddleName: null, LastName: "Reyes", Suffix: null,
            Birthdate: null, Gender: null, CivilStatus: null,
            EducationLevel: null, SchoolName: null, CourseYearGraduated: null, SpecifiedProfession: null,
            MobileNumber: null,
            HouseNo: null, Street: null, Barangay: null, CityMunicipality: null, Province: null, ZipCode: null, Country: null,
            MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null, MailingCountry: null,
            HousePhone: null,
            PrcLicenseNo: null, PrcRegistrationDate: null, PrcValidUntilDate: null, PtrNumber: null, PtrPlaceIssued: null, PtrDateIssued: null, Tin: null, Chapter: Chapters.Davao, ChapterYear: null, ChapterPosition: null,
            EmploymentStatus: null, Company: null, Position: null, BusinessAddress: null, YearsOfPractice: null, Specialization: null, Skills: null,
            MemberType: MemberTypes.Regular);

        var result = await controller.UpdateMyProfile(request, CancellationToken.None);

        var dto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(MembershipStatus.Pending, dto.Status);
    }

    [Fact]
    public async Task Update_ChangesFieldsIncludingStatus()
    {
        var user = await CreateUserAsync();
        var controller = CreateController();
        var created = await controller.Create(BuildCreateRequest(user.Id), CancellationToken.None);
        var createdDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(created.Result).Value);

        var updateRequest = new UpdateMemberRequest(
            FirstName: "Juan", MiddleName: null, LastName: "Dela Cruz", Suffix: null,
            Birthdate: createdDto.Birthdate, Gender: createdDto.Gender, CivilStatus: createdDto.CivilStatus,
            EducationLevel: createdDto.EducationLevel, SchoolName: createdDto.SchoolName, CourseYearGraduated: createdDto.CourseYearGraduated, SpecifiedProfession: createdDto.SpecifiedProfession,
            MobileNumber: createdDto.MobileNumber,
            HouseNo: createdDto.HouseNo, Street: createdDto.Street, Barangay: createdDto.Barangay, CityMunicipality: createdDto.CityMunicipality, Province: createdDto.Province, ZipCode: createdDto.ZipCode, Country: createdDto.Country,
            MailingHouseNo: createdDto.MailingHouseNo, MailingStreet: createdDto.MailingStreet, MailingBarangay: createdDto.MailingBarangay,
            MailingCityMunicipality: createdDto.MailingCityMunicipality, MailingProvince: createdDto.MailingProvince, MailingZipCode: createdDto.MailingZipCode, MailingCountry: createdDto.MailingCountry,
            HousePhone: null,
            PrcLicenseNo: createdDto.PrcLicenseNo, PrcRegistrationDate: createdDto.PrcRegistrationDate, PrcValidUntilDate: createdDto.PrcValidUntilDate,
            PtrNumber: createdDto.PtrNumber, PtrPlaceIssued: createdDto.PtrPlaceIssued, PtrDateIssued: createdDto.PtrDateIssued, Tin: createdDto.Tin,
            Chapter: createdDto.Chapter, ChapterYear: createdDto.ChapterYear, ChapterPosition: createdDto.ChapterPosition,
            EmploymentStatus: null, Company: createdDto.Company, Position: null, BusinessAddress: null,
            YearsOfPractice: null, Specialization: null, Skills: null,
            MemberType: createdDto.MemberType, Status: MembershipStatus.Active, RenewalDueDate: createdDto.RenewalDueDate,
            NationalDuesReferenceNo: createdDto.NationalDuesReferenceNo);

        var result = await controller.Update(createdDto.Id, updateRequest, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var updated = await controller.GetById(createdDto.Id, CancellationToken.None);
        var updatedDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(updated.Result).Value);
        Assert.Equal(MembershipStatus.Active, updatedDto.Status);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFound()
    {
        var controller = CreateController();
        var request = new UpdateMemberRequest(
            FirstName: "First", MiddleName: null, LastName: "Last", Suffix: null,
            Birthdate: null, Gender: null, CivilStatus: null,
            EducationLevel: null, SchoolName: null, CourseYearGraduated: null, SpecifiedProfession: null,
            MobileNumber: null,
            HouseNo: null, Street: null, Barangay: null, CityMunicipality: null, Province: null, ZipCode: null, Country: null,
            MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null, MailingCountry: null,
            HousePhone: null,
            PrcLicenseNo: null, PrcRegistrationDate: null, PrcValidUntilDate: null, PtrNumber: null, PtrPlaceIssued: null, PtrDateIssued: null, Tin: null, Chapter: Chapters.Ncr, ChapterYear: null, ChapterPosition: null,
            EmploymentStatus: null, Company: null, Position: null, BusinessAddress: null,
            YearsOfPractice: null, Specialization: null, Skills: null,
            MemberType: MemberTypes.Regular, Status: MembershipStatus.Active, RenewalDueDate: null, NationalDuesReferenceNo: null);

        var result = await controller.Update(Guid.NewGuid(), request, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Delete_RemovesMemberProfile_ButNotUnderlyingUser()
    {
        var user = await CreateUserAsync();
        var controller = CreateController();
        var created = await controller.Create(BuildCreateRequest(user.Id), CancellationToken.None);
        var createdDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(created.Result).Value);

        var result = await controller.Delete(createdDto.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var afterDelete = await controller.GetById(createdDto.Id, CancellationToken.None);
        Assert.IsType<NotFoundResult>(afterDelete.Result);
        Assert.NotNull(await _userManager.FindByIdAsync(user.Id.ToString()));
    }

    [Fact]
    public async Task Approve_SetsApprovedAt_AndIsIdempotent()
    {
        var user = await CreateUserAsync();
        var controller = CreateController();
        var created = await controller.Create(BuildCreateRequest(user.Id), CancellationToken.None);
        var createdDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(created.Result).Value);
        Assert.Null(createdDto.ApprovedAt);

        await VerifyRmpAsync(controller, createdDto.Id);

        var firstApprove = await controller.Approve(createdDto.Id, ApproveWithPayment("A-0001"), CancellationToken.None);
        Assert.IsType<NoContentResult>(firstApprove);

        var afterFirst = await controller.GetById(createdDto.Id, CancellationToken.None);
        var afterFirstDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(afterFirst.Result).Value);
        Assert.NotNull(afterFirstDto.ApprovedAt);

        var secondApprove = await controller.Approve(createdDto.Id, ApproveWithPayment("A-0002"), CancellationToken.None);
        Assert.IsType<NoContentResult>(secondApprove);

        var afterSecond = await controller.GetById(createdDto.Id, CancellationToken.None);
        var afterSecondDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(afterSecond.Result).Value);
        Assert.Equal(afterFirstDto.ApprovedAt, afterSecondDto.ApprovedAt);
        // The second call passed a different number on purpose: a repeat approval must not
        // renumber a live member.
        Assert.Equal("A-0001", afterSecondDto.MembershipNo);
    }

    /// <summary>
    /// The reported scenario: a member sitting in both queues could be admitted to PSMPE while
    /// their RMP licence had never been checked. Approving issues a control number, generates a
    /// receipt and emails the member, so the licence has to be confirmed first.
    /// </summary>
    [Fact]
    public async Task Approve_WithAnUnverifiedRmpLicence_IsRejectedAndDoesNotApprove()
    {
        var user = await CreateUserAsync();
        var controller = CreateController();
        var created = await controller.Create(BuildCreateRequest(user.Id), CancellationToken.None);
        var createdDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(created.Result).Value);
        Assert.False(createdDto.PrcIdVerified);

        var result = await controller.Approve(createdDto.Id, ApproveWithPayment("RMP-GATE-1"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        var fetched = await controller.GetById(createdDto.Id, CancellationToken.None);
        var dto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(fetched.Result).Value);
        Assert.Null(dto.ApprovedAt);
        // The number must not be assigned either - a blocked approval leaves nothing behind.
        Assert.NotEqual("RMP-GATE-1", dto.MembershipNo);
    }

    /// <summary>
    /// Approval and payment are one act now. A member with no payment on record and none supplied
    /// can't be admitted - otherwise the admin form would be a way to bypass payment entirely.
    /// </summary>
    [Fact]
    public async Task Approve_WithNoPaymentOnRecordAndNoneSupplied_IsRejected()
    {
        var user = await CreateUserAsync();
        var controller = CreateController();
        var created = await controller.Create(BuildCreateRequest(user.Id), CancellationToken.None);
        var dto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(created.Result).Value);
        await VerifyRmpAsync(controller, dto.Id);

        var result = await controller.Approve(dto.Id, new ApproveMemberRequest("PAY-GATE-1"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        var fetched = await controller.GetById(dto.Id, CancellationToken.None);
        var after = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(fetched.Result).Value);
        Assert.Null(after.ApprovedAt);
        Assert.NotEqual("PAY-GATE-1", after.MembershipNo);
    }

    /// <summary>
    /// The whole point of doing both in one transaction: there is no observable state where the
    /// member is approved but still unpaid.
    /// </summary>
    [Fact]
    public async Task Approve_AcceptsTheRegistrationPaymentInTheSameTransaction()
    {
        var user = await CreateUserAsync();
        var controller = CreateController();
        var created = await controller.Create(BuildCreateRequest(user.Id), CancellationToken.None);
        var dto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(created.Result).Value);
        await VerifyRmpAsync(controller, dto.Id);

        Assert.IsType<NoContentResult>(
            await controller.Approve(dto.Id, ApproveWithPayment("PAY-GATE-2"), CancellationToken.None));

        var fetched = await controller.GetById(dto.Id, CancellationToken.None);
        var after = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(fetched.Result).Value);
        Assert.NotNull(after.ApprovedAt);
        Assert.Equal(MembershipStatus.Active, after.Status);
        // Set by the payment half of the same operation - proof both ran.
        Assert.Equal(DateOnly.FromDateTime(after.ApprovedAt!.Value.UtcDateTime).AddYears(1), after.RenewalDueDate);

        var payments = await _paymentService.GetForMemberAsync(dto.Id);
        var payment = Assert.Single(payments);
        Assert.Equal(PaymentStatus.Verified, payment.Status);
        Assert.Equal(after.RenewalDueDate, payment.CoversUntil);
    }

    /// <summary>
    /// A self-service applicant already has a payment (created at submit), so the admin reviews it
    /// rather than entering one. Supplying details anyway is refused rather than silently dropped -
    /// otherwise the admin would think they had corrected an amount that never changed.
    /// </summary>
    [Fact]
    public async Task Approve_SupplyingAPaymentWhenOneAlreadyExists_IsRejected()
    {
        var user = await CreateUserAsync();
        var controller = CreateController();
        var created = await controller.Create(BuildCreateRequest(user.Id), CancellationToken.None);
        var dto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(created.Result).Value);
        await VerifyRmpAsync(controller, dto.Id);

        // Stands in for the self-service path, where submitting the application already created a
        // NewMembership payment for the admin to review.
        var db = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Payments.Add(new Payment
        {
            MemberId = dto.Id,
            Kind = PaymentKind.NewMembership,
            Amount = 1700m,
            PaidOn = DateOnly.FromDateTime(DateTime.UtcNow),
            ProofStorageKey = "existing/proof.jpg",
            Status = PaymentStatus.Submitted,
        });
        await db.SaveChangesAsync();

        var result = await controller.Approve(dto.Id, ApproveWithPayment("PAY-GATE-3"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        var fetched = await controller.GetById(dto.Id, CancellationToken.None);
        Assert.Null(Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(fetched.Result).Value).ApprovedAt);
    }

    [Fact]
    public async Task Approve_AfterVerifyingTheRmpLicence_Succeeds()
    {
        var user = await CreateUserAsync();
        var controller = CreateController();
        var created = await controller.Create(BuildCreateRequest(user.Id), CancellationToken.None);
        var createdDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(created.Result).Value);

        await VerifyRmpAsync(controller, createdDto.Id);
        var result = await controller.Approve(createdDto.Id, ApproveWithPayment("RMP-GATE-2"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var fetched = await controller.GetById(createdDto.Id, CancellationToken.None);
        var dto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(fetched.Result).Value);
        Assert.NotNull(dto.ApprovedAt);
        Assert.Equal("RMP-GATE-2", dto.MembershipNo);
    }

    [Fact]
    public async Task Approve_AssignsTheMembershipNoFromTheRequest()
    {
        var user = await CreateUserAsync();
        var controller = CreateController();
        var created = await controller.Create(BuildCreateRequest(user.Id), CancellationToken.None);
        var createdDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(created.Result).Value);

        // Padded so the trim is exercised - admins paste these out of spreadsheets.
        await VerifyRmpAsync(controller, createdDto.Id);
        var result = await controller.Approve(createdDto.Id, ApproveWithPayment("  PSMPE-2026-000123  "), CancellationToken.None);
        Assert.IsType<NoContentResult>(result);

        var fetched = await controller.GetById(createdDto.Id, CancellationToken.None);
        var dto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(fetched.Result).Value);
        Assert.Equal("PSMPE-2026-000123", dto.MembershipNo);
        Assert.NotNull(dto.ApprovedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Approve_WithoutAMembershipNo_IsRejectedAndDoesNotApprove(string membershipNo)
    {
        var user = await CreateUserAsync();
        var controller = CreateController();
        var created = await controller.Create(BuildCreateRequest(user.Id), CancellationToken.None);
        var createdDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(created.Result).Value);

        await VerifyRmpAsync(controller, createdDto.Id);

        var result = await controller.Approve(createdDto.Id, ApproveWithPayment(membershipNo), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        var fetched = await controller.GetById(createdDto.Id, CancellationToken.None);
        var dto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(fetched.Result).Value);
        Assert.Null(dto.ApprovedAt);
    }

    [Fact]
    public async Task Approve_WithAMembershipNoAnotherMemberHolds_ReturnsConflict()
    {
        var controller = CreateController();

        var firstUser = await CreateUserAsync();
        var firstCreated = await controller.Create(BuildCreateRequest(firstUser.Id), CancellationToken.None);
        var firstDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(firstCreated.Result).Value);
        await VerifyRmpAsync(controller, firstDto.Id);
        Assert.IsType<NoContentResult>(
            await controller.Approve(firstDto.Id, ApproveWithPayment("DUPLICATE-1"), CancellationToken.None));

        var secondUser = await CreateUserAsync();
        var secondCreated = await controller.Create(BuildCreateRequest(secondUser.Id), CancellationToken.None);
        var secondDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(secondCreated.Result).Value);

        await VerifyRmpAsync(controller, secondDto.Id);

        var result = await controller.Approve(secondDto.Id, ApproveWithPayment("DUPLICATE-1"), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        var fetched = await controller.GetById(secondDto.Id, CancellationToken.None);
        var dto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(fetched.Result).Value);
        Assert.Null(dto.ApprovedAt);
    }

    /// <summary>
    /// A control number differing only in case is the same number to every human reading it. The
    /// original byte comparison let both through, putting two members on one ID.
    /// </summary>
    // Distinct number per case: this class shares one database across its tests, so reusing a
    // single literal would make the second case collide with the first case's own seed.
    [Theory]
    [InlineData("PSMPE-CI1", "psmpe-ci1")]
    [InlineData("psmpe-ci2", "PSMPE-CI2")]
    [InlineData("PSMPE-CI3", "PsMpE-cI3")]
    [InlineData("PSMPE-CI4", "  psmpe-ci4  ")]
    public async Task Approve_WithAMembershipNoDifferingOnlyInCase_ReturnsConflict(string original, string secondAttempt)
    {
        var controller = CreateController();

        var firstUser = await CreateUserAsync();
        var firstCreated = await controller.Create(BuildCreateRequest(firstUser.Id), CancellationToken.None);
        var firstDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(firstCreated.Result).Value);
        await VerifyRmpAsync(controller, firstDto.Id);
        Assert.IsType<NoContentResult>(
            await controller.Approve(firstDto.Id, ApproveWithPayment(original), CancellationToken.None));

        var secondUser = await CreateUserAsync();
        var secondCreated = await controller.Create(BuildCreateRequest(secondUser.Id), CancellationToken.None);
        var secondDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(secondCreated.Result).Value);

        await VerifyRmpAsync(controller, secondDto.Id);

        var result = await controller.Approve(secondDto.Id, ApproveWithPayment(secondAttempt), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        var fetched = await controller.GetById(secondDto.Id, CancellationToken.None);
        var dto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(fetched.Result).Value);
        Assert.Null(dto.ApprovedAt);
    }

    [Fact]
    public async Task CheckMembershipNoAvailability_ReportsFreeAndTakenNumbers()
    {
        var controller = CreateController();
        var user = await CreateUserAsync();
        var created = await controller.Create(BuildCreateRequest(user.Id), CancellationToken.None);
        var dto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(created.Result).Value);
        await VerifyRmpAsync(controller, dto.Id);
        await controller.Approve(dto.Id, ApproveWithPayment("PSMPE-777"), CancellationToken.None);

        var free = await controller.CheckMembershipNoAvailability("PSMPE-778", null, CancellationToken.None);
        Assert.True(Assert.IsType<MembershipNoAvailabilityDto>(Assert.IsType<OkObjectResult>(free.Result).Value).IsAvailable);

        // Case-insensitive here too, or the dialog would say "available" for a number approve then
        // rejects - worse than not checking at all.
        var taken = await controller.CheckMembershipNoAvailability("psmpe-777", null, CancellationToken.None);
        Assert.False(Assert.IsType<MembershipNoAvailabilityDto>(Assert.IsType<OkObjectResult>(taken.Result).Value).IsAvailable);

        // Excluding the holder lets the correction path re-submit a member's own number unchanged.
        var ownNumber = await controller.CheckMembershipNoAvailability("PSMPE-777", dto.Id, CancellationToken.None);
        Assert.True(Assert.IsType<MembershipNoAvailabilityDto>(Assert.IsType<OkObjectResult>(ownNumber.Result).Value).IsAvailable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CheckMembershipNoAvailability_WithNothingToCheck_ReportsUnavailable(string value)
    {
        var controller = CreateController();

        var result = await controller.CheckMembershipNoAvailability(value, null, CancellationToken.None);

        // Not "available" - a blank ID can't be approved, and saying otherwise would be misleading.
        Assert.False(Assert.IsType<MembershipNoAvailabilityDto>(Assert.IsType<OkObjectResult>(result.Result).Value).IsAvailable);
    }

    [Fact]
    public async Task CheckMembershipNoAvailability_OverLengthValue_ReportsUnavailable()
    {
        var controller = CreateController();

        var result = await controller.CheckMembershipNoAvailability(new string('X', 33), null, CancellationToken.None);

        Assert.False(Assert.IsType<MembershipNoAvailabilityDto>(Assert.IsType<OkObjectResult>(result.Result).Value).IsAvailable);
    }

    [Fact]
    public async Task Approve_GeneratesDownloadableReceipt()
    {
        var user = await CreateUserAsync();
        var adminController = CreateController();
        var created = await adminController.Create(BuildCreateRequest(user.Id), CancellationToken.None);
        var createdDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(created.Result).Value);

        var beforeApprove = await CreateController(user.Id).GetMyReceipt(CancellationToken.None);
        Assert.IsType<NotFoundResult>(beforeApprove);

        await VerifyRmpAsync(adminController, createdDto.Id);
        var approveResult = await adminController.Approve(createdDto.Id, ApproveWithPayment("A-0003"), CancellationToken.None);
        Assert.IsType<NoContentResult>(approveResult);

        var afterApprove = await CreateController(user.Id).GetMyReceipt(CancellationToken.None);
        var fileResult = Assert.IsType<FileStreamResult>(afterApprove);
        Assert.Equal("image/jpeg", fileResult.ContentType);
    }

    [Fact]
    public async Task Approve_Twice_DoesNotFailOnAlreadyGeneratedReceipt()
    {
        var user = await CreateUserAsync();
        var adminController = CreateController();
        var created = await adminController.Create(BuildCreateRequest(user.Id), CancellationToken.None);
        var createdDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(created.Result).Value);

        await VerifyRmpAsync(adminController, createdDto.Id);
        Assert.IsType<NoContentResult>(await adminController.Approve(createdDto.Id, ApproveWithPayment(Guid.NewGuid().ToString("N")[..8]), CancellationToken.None));
        Assert.IsType<NoContentResult>(await adminController.Approve(createdDto.Id, ApproveWithPayment(Guid.NewGuid().ToString("N")[..8]), CancellationToken.None));

        var afterSecondApprove = await CreateController(user.Id).GetMyReceipt(CancellationToken.None);
        Assert.IsType<FileStreamResult>(afterSecondApprove);
    }

    [Fact]
    public async Task Approve_UnknownId_ReturnsNotFound()
    {
        var controller = CreateController();

        var result = await controller.Approve(Guid.NewGuid(), ApproveWithPayment("A-0006"), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetAll_WithStatusFilter_ReturnsOnlyMatchingMembers()
    {
        var pendingUser = await CreateUserAsync();
        var activeUser = await CreateUserAsync();
        var controller = CreateController();
        var pendingCreated = await controller.Create(BuildCreateRequest(pendingUser.Id), CancellationToken.None);
        var pendingDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(pendingCreated.Result).Value);
        var activeCreated = await controller.Create(BuildCreateRequest(activeUser.Id), CancellationToken.None);
        var activeDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(activeCreated.Result).Value);

        var activateRequest = new UpdateMemberRequest(
            FirstName: activeDto.FirstName, MiddleName: activeDto.MiddleName, LastName: activeDto.LastName, Suffix: activeDto.Suffix,
            Birthdate: activeDto.Birthdate, Gender: activeDto.Gender, CivilStatus: activeDto.CivilStatus,
            EducationLevel: activeDto.EducationLevel, SchoolName: activeDto.SchoolName, CourseYearGraduated: activeDto.CourseYearGraduated, SpecifiedProfession: activeDto.SpecifiedProfession,
            MobileNumber: activeDto.MobileNumber,
            HouseNo: activeDto.HouseNo, Street: activeDto.Street, Barangay: activeDto.Barangay, CityMunicipality: activeDto.CityMunicipality, Province: activeDto.Province, ZipCode: activeDto.ZipCode, Country: activeDto.Country,
            MailingHouseNo: activeDto.MailingHouseNo, MailingStreet: activeDto.MailingStreet, MailingBarangay: activeDto.MailingBarangay,
            MailingCityMunicipality: activeDto.MailingCityMunicipality, MailingProvince: activeDto.MailingProvince, MailingZipCode: activeDto.MailingZipCode, MailingCountry: activeDto.MailingCountry,
            HousePhone: activeDto.HousePhone,
            PrcLicenseNo: activeDto.PrcLicenseNo, PrcRegistrationDate: activeDto.PrcRegistrationDate, PrcValidUntilDate: activeDto.PrcValidUntilDate,
            PtrNumber: activeDto.PtrNumber, PtrPlaceIssued: activeDto.PtrPlaceIssued, PtrDateIssued: activeDto.PtrDateIssued, Tin: activeDto.Tin,
            Chapter: activeDto.Chapter, ChapterYear: activeDto.ChapterYear, ChapterPosition: activeDto.ChapterPosition, EmploymentStatus: activeDto.EmploymentStatus, Company: activeDto.Company, Position: activeDto.Position, BusinessAddress: activeDto.BusinessAddress,
            YearsOfPractice: activeDto.YearsOfPractice, Specialization: activeDto.Specialization, Skills: activeDto.Skills,
            MemberType: activeDto.MemberType, Status: MembershipStatus.Active,
            RenewalDueDate: activeDto.RenewalDueDate, NationalDuesReferenceNo: activeDto.NationalDuesReferenceNo);
        await controller.Update(activeDto.Id, activateRequest, CancellationToken.None);

        var result = await controller.GetAll(page: 1, pageSize: 1000, status: MembershipStatus.Pending, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var paged = Assert.IsType<PagedResult<MemberDto>>(ok.Value);
        Assert.Contains(paged.Items, m => m.Id == pendingDto.Id);
        Assert.DoesNotContain(paged.Items, m => m.Id == activeDto.Id);
    }

    [Fact]
    public async Task GetAll_WithPendingApprovalOnly_ExcludesApprovedMembers_EvenIfStillStatusPending()
    {
        var unapprovedUser = await CreateUserAsync();
        var approvedUser = await CreateUserAsync();
        var controller = CreateController();
        var unapprovedCreated = await controller.Create(BuildCreateRequest(unapprovedUser.Id), CancellationToken.None);
        var unapprovedDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(unapprovedCreated.Result).Value);
        var approvedCreated = await controller.Create(BuildCreateRequest(approvedUser.Id), CancellationToken.None);
        var approvedDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(approvedCreated.Result).Value);
        await VerifyRmpAsync(controller, approvedDto.Id);
        await controller.Approve(approvedDto.Id, ApproveWithPayment("A-0007"), CancellationToken.None);

        var result = await controller.GetAll(page: 1, pageSize: 1000, pendingApprovalOnly: true, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var paged = Assert.IsType<PagedResult<MemberDto>>(ok.Value);
        Assert.Contains(paged.Items, m => m.Id == unapprovedDto.Id);
        Assert.DoesNotContain(paged.Items, m => m.Id == approvedDto.Id);
    }

    [Theory]
    // Grace period is 7 days (MembershipGracePeriod.DefaultDays) - -3 is inside the window, -40 is
    // well past it, +10 hasn't lapsed yet.
    [InlineData(-3, true)]
    [InlineData(-40, false)]
    [InlineData(10, false)]
    public async Task GetById_IsInGracePeriod_ReflectsRenewalDueDateWindow(int dueDateOffsetDays, bool expectedInGrace)
    {
        var user = await CreateUserAsync();
        var controller = CreateController();
        var created = await controller.Create(BuildCreateRequest(user.Id), CancellationToken.None);
        var createdDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(created.Result).Value);

        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(dueDateOffsetDays));
        var updateRequest = new UpdateMemberRequest(
            FirstName: createdDto.FirstName, MiddleName: createdDto.MiddleName, LastName: createdDto.LastName, Suffix: createdDto.Suffix,
            Birthdate: createdDto.Birthdate, Gender: createdDto.Gender, CivilStatus: createdDto.CivilStatus,
            EducationLevel: createdDto.EducationLevel, SchoolName: createdDto.SchoolName, CourseYearGraduated: createdDto.CourseYearGraduated, SpecifiedProfession: createdDto.SpecifiedProfession,
            MobileNumber: createdDto.MobileNumber,
            HouseNo: createdDto.HouseNo, Street: createdDto.Street, Barangay: createdDto.Barangay, CityMunicipality: createdDto.CityMunicipality, Province: createdDto.Province, ZipCode: createdDto.ZipCode, Country: createdDto.Country,
            MailingHouseNo: createdDto.MailingHouseNo, MailingStreet: createdDto.MailingStreet, MailingBarangay: createdDto.MailingBarangay,
            MailingCityMunicipality: createdDto.MailingCityMunicipality, MailingProvince: createdDto.MailingProvince, MailingZipCode: createdDto.MailingZipCode, MailingCountry: createdDto.MailingCountry,
            HousePhone: createdDto.HousePhone,
            PrcLicenseNo: createdDto.PrcLicenseNo, PrcRegistrationDate: createdDto.PrcRegistrationDate, PrcValidUntilDate: createdDto.PrcValidUntilDate,
            PtrNumber: createdDto.PtrNumber, PtrPlaceIssued: createdDto.PtrPlaceIssued, PtrDateIssued: createdDto.PtrDateIssued, Tin: createdDto.Tin,
            Chapter: createdDto.Chapter, ChapterYear: createdDto.ChapterYear, ChapterPosition: createdDto.ChapterPosition, EmploymentStatus: createdDto.EmploymentStatus, Company: createdDto.Company, Position: createdDto.Position, BusinessAddress: createdDto.BusinessAddress,
            YearsOfPractice: createdDto.YearsOfPractice, Specialization: createdDto.Specialization, Skills: createdDto.Skills,
            MemberType: createdDto.MemberType, Status: MembershipStatus.Active,
            RenewalDueDate: dueDate, NationalDuesReferenceNo: createdDto.NationalDuesReferenceNo);
        await controller.Update(createdDto.Id, updateRequest, CancellationToken.None);

        var result = await controller.GetById(createdDto.Id, CancellationToken.None);
        var dto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(expectedInGrace, dto.IsInGracePeriod);
    }

    [Fact]
    public async Task SubmitMyProfile_NoDraftYet_ReturnsNotFound()
    {
        var user = await CreateUserAsync();
        var controller = CreateController(user.Id);

        var result = await controller.SubmitMyProfile(CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task SubmitMyProfile_MissingRequiredFields_ReturnsBadRequest()
    {
        var user = await CreateUserAsync();
        var controller = CreateController(user.Id);
        var request = new UpdateMyProfileRequest(
            FirstName: "", MiddleName: null, LastName: "", Suffix: null,
            Birthdate: null, Gender: null, CivilStatus: null,
            EducationLevel: null, SchoolName: null, CourseYearGraduated: null, SpecifiedProfession: null,
            MobileNumber: null,
            HouseNo: null, Street: null, Barangay: null, CityMunicipality: null, Province: null, ZipCode: null, Country: null,
            MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null, MailingCountry: null,
            HousePhone: null,
            PrcLicenseNo: null, PrcRegistrationDate: null, PrcValidUntilDate: null, PtrNumber: null, PtrPlaceIssued: null, PtrDateIssued: null, Tin: null, Chapter: "", ChapterYear: null, ChapterPosition: null,
            EmploymentStatus: null, Company: null, Position: null, BusinessAddress: null, YearsOfPractice: null, Specialization: null, Skills: null,
            MemberType: "");
        await controller.UpdateMyProfile(request, CancellationToken.None);

        var result = await controller.SubmitMyProfile(CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private static UpdateMyProfileRequest BuildCompleteProfileRequest(DateOnly birthdate) => new(
        FirstName: "Draft", MiddleName: null, LastName: "Applicant", Suffix: null,
        Birthdate: birthdate, Gender: "Male", CivilStatus: "Single",
        EducationLevel: "College / University", SchoolName: "Sample University", CourseYearGraduated: "BSCE 2015", SpecifiedProfession: "Master Plumber",
        MobileNumber: "09171234567",
        HouseNo: null, Street: "123 Sample St", Barangay: "Sample Barangay", CityMunicipality: "Sample City", Province: "Sample Province", ZipCode: "1000", Country: "Philippines",
        MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null, MailingCountry: null,
        HousePhone: null,
        PrcLicenseNo: "MP 99999", PrcRegistrationDate: new DateOnly(2020, 1, 1), PrcValidUntilDate: new DateOnly(2030, 1, 1),
        PtrNumber: "PTR-0099999", PtrPlaceIssued: null, PtrDateIssued: null, Tin: null,
        Chapter: Chapters.Ncr, ChapterYear: null, ChapterPosition: null,
        EmploymentStatus: null, Company: null, Position: null, BusinessAddress: null, YearsOfPractice: null, Specialization: null, Skills: null,
        MemberType: MemberTypes.Regular);

    private static byte[] BuildPng(int width = 50, int height = 50)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private async Task UploadPhotoAsync(Guid userId)
    {
        var bytes = BuildPng();
        await using var stream = new MemoryStream(bytes);
        var uploadResult = await _memberUploadService.UploadAsync(userId, UploadKind.Photo, stream, "photo.png", stream.Length, CancellationToken.None);
        Assert.True(uploadResult.Succeeded);
    }

    private async Task UploadProofOfPaymentAsync(Guid userId)
    {
        var bytes = BuildPng();
        await using var stream = new MemoryStream(bytes);
        var uploadResult = await _memberUploadService.UploadAsync(userId, UploadKind.ProofOfPayment, stream, "receipt.png", stream.Length, CancellationToken.None);
        Assert.True(uploadResult.Succeeded);
    }

    [Fact]
    public async Task SubmitMyProfile_BirthdateUnder18_ReturnsBadRequest()
    {
        var user = await CreateUserAsync();
        var controller = CreateController(user.Id);
        var seventeenYearsAgo = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-17));
        await controller.UpdateMyProfile(BuildCompleteProfileRequest(seventeenYearsAgo), CancellationToken.None);

        var result = await controller.SubmitMyProfile(CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SubmitMyProfile_BirthdateExactly18YearsAgo_Succeeds()
    {
        var user = await CreateUserAsync();
        var controller = CreateController(user.Id);
        var exactlyEighteen = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-18));
        await controller.UpdateMyProfile(BuildCompleteProfileRequest(exactlyEighteen), CancellationToken.None);
        await UploadFreshPrcIdAsync(user.Id);
        await UploadPhotoAsync(user.Id);
        await UploadProofOfPaymentAsync(user.Id);

        var result = await controller.SubmitMyProfile(CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Theory]
    [InlineData("prcLicenseNo")]
    [InlineData("gender")]
    [InlineData("civilStatus")]
    [InlineData("street")]
    [InlineData("mobileNumber")]
    public async Task SubmitMyProfile_MissingAnyNewRequiredField_ReturnsBadRequest(string fieldToOmit)
    {
        var user = await CreateUserAsync();
        var controller = CreateController(user.Id);
        var complete = BuildCompleteProfileRequest(new DateOnly(1990, 1, 1));
        var request = fieldToOmit switch
        {
            "prcLicenseNo" => complete with { PrcLicenseNo = null },
            "gender" => complete with { Gender = null },
            "civilStatus" => complete with { CivilStatus = null },
            "street" => complete with { Street = null },
            "mobileNumber" => complete with { MobileNumber = null },
            _ => throw new ArgumentOutOfRangeException(nameof(fieldToOmit)),
        };
        await controller.UpdateMyProfile(request, CancellationToken.None);
        await UploadFreshPrcIdAsync(user.Id);
        await UploadPhotoAsync(user.Id);
        await UploadProofOfPaymentAsync(user.Id);

        var result = await controller.SubmitMyProfile(CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    /// <summary>
    /// PTR Number used to be required to submit and no longer is, along with the rest of the
    /// Additional Information step. Pins that, since the step now has no required field at all -
    /// a regression here would silently start blocking applicants again.
    /// </summary>
    [Fact]
    public async Task SubmitMyProfile_WithoutPtrNumber_Succeeds()
    {
        var user = await CreateUserAsync();
        var controller = CreateController(user.Id);
        var complete = BuildCompleteProfileRequest(new DateOnly(1990, 1, 1));
        await controller.UpdateMyProfile(complete with { PtrNumber = null, Tin = null, Company = null }, CancellationToken.None);
        await UploadFreshPrcIdAsync(user.Id);
        await UploadPhotoAsync(user.Id);
        await UploadProofOfPaymentAsync(user.Id);

        var result = await controller.SubmitMyProfile(CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task SubmitMyProfile_WithRequiredFieldsFilled_SetsSubmittedAt_AndIsIdempotent()
    {
        var user = await CreateUserAsync();
        var controller = CreateController(user.Id);
        var request = BuildCompleteProfileRequest(new DateOnly(1990, 1, 1));
        await controller.UpdateMyProfile(request, CancellationToken.None);
        await UploadFreshPrcIdAsync(user.Id);
        await UploadPhotoAsync(user.Id);
        await UploadProofOfPaymentAsync(user.Id);

        var firstSubmit = await controller.SubmitMyProfile(CancellationToken.None);
        Assert.IsType<NoContentResult>(firstSubmit);

        var afterFirst = await controller.GetMyProfile(CancellationToken.None);
        var afterFirstDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(afterFirst.Result).Value);
        Assert.NotNull(afterFirstDto.SubmittedAt);

        var secondSubmit = await controller.SubmitMyProfile(CancellationToken.None);
        Assert.IsType<NoContentResult>(secondSubmit);

        var afterSecond = await controller.GetMyProfile(CancellationToken.None);
        var afterSecondDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(afterSecond.Result).Value);
        Assert.Equal(afterFirstDto.SubmittedAt, afterSecondDto.SubmittedAt);
    }

    [Fact]
    public async Task GetAll_ExcludesUnsubmittedDrafts_EvenWhenPendingApprovalOnlyIsFalse()
    {
        var draftUser = await CreateUserAsync();
        var adminController = CreateController();
        var draftController = CreateController(draftUser.Id);
        var draftRequest = new UpdateMyProfileRequest(
            FirstName: "Still", MiddleName: null, LastName: "Drafting", Suffix: null,
            Birthdate: null, Gender: null, CivilStatus: null,
            EducationLevel: null, SchoolName: null, CourseYearGraduated: null, SpecifiedProfession: null,
            MobileNumber: null,
            HouseNo: null, Street: null, Barangay: null, CityMunicipality: null, Province: null, ZipCode: null, Country: null,
            MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null, MailingCountry: null,
            HousePhone: null,
            PrcLicenseNo: null, PrcRegistrationDate: null, PrcValidUntilDate: null, PtrNumber: null, PtrPlaceIssued: null, PtrDateIssued: null, Tin: null, Chapter: Chapters.Ncr, ChapterYear: null, ChapterPosition: null,
            EmploymentStatus: null, Company: null, Position: null, BusinessAddress: null, YearsOfPractice: null, Specialization: null, Skills: null,
            MemberType: MemberTypes.Regular);
        var draftResult = await draftController.UpdateMyProfile(draftRequest, CancellationToken.None);
        var draftDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(draftResult.Result).Value);
        Assert.Null(draftDto.SubmittedAt);

        var result = await adminController.GetAll(page: 1, pageSize: 1000, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var paged = Assert.IsType<PagedResult<MemberDto>>(ok.Value);
        Assert.DoesNotContain(paged.Items, m => m.Id == draftDto.Id);
    }

    private static UpdateMyProfileRequest BuildProfileRequest(
        string chapter, string memberType, string? prcLicenseNo = null, bool prcIdReuploaded = false,
        DateOnly? prcRegistrationDate = null, DateOnly? prcValidUntilDate = null) => new(
        FirstName: "Juan", MiddleName: null, LastName: "Dela Cruz", Suffix: null,
        Birthdate: new DateOnly(1990, 1, 1), Gender: "Male", CivilStatus: "Single",
        EducationLevel: "College / University", SchoolName: "Sample University", CourseYearGraduated: "BSCE 2015", SpecifiedProfession: "Master Plumber",
        MobileNumber: "09171234567",
        HouseNo: null, Street: "123 Main St", Barangay: "Sample Barangay", CityMunicipality: "Sample City", Province: "Sample Province", ZipCode: "1000", Country: "Philippines",
        MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null, MailingCountry: null,
        HousePhone: null,
        PrcLicenseNo: prcLicenseNo, PrcRegistrationDate: prcRegistrationDate ?? new DateOnly(2020, 1, 1), PrcValidUntilDate: prcValidUntilDate ?? new DateOnly(2030, 1, 1),
        PtrNumber: "PTR-0012345", PtrPlaceIssued: null, PtrDateIssued: null, Tin: null,
        Chapter: chapter, ChapterYear: null, ChapterPosition: null,
        EmploymentStatus: null, Company: null, Position: null, BusinessAddress: null, YearsOfPractice: null, Specialization: null, Skills: null,
        MemberType: memberType,
        PrcIdReuploaded: prcIdReuploaded);

    private async Task<Guid> CreateSubmittedApplicantAsync(string? prcLicenseNo = null)
    {
        var user = await CreateUserAsync();
        var controller = CreateController(user.Id);
        await controller.UpdateMyProfile(BuildProfileRequest(Chapters.Ncr, MemberTypes.Regular, prcLicenseNo), CancellationToken.None);
        await UploadFreshPrcIdAsync(user.Id);
        await UploadPhotoAsync(user.Id);
        await UploadProofOfPaymentAsync(user.Id);
        await controller.SubmitMyProfile(CancellationToken.None);
        return user.Id;
    }

    [Fact]
    public async Task UpdateMyProfile_AfterSubmit_CraftedMemberTypeOrChapterChange_ReturnsBadRequest()
    {
        var userId = await CreateSubmittedApplicantAsync(prcLicenseNo: "MP-0001");
        var controller = CreateController(userId);

        var result = await controller.UpdateMyProfile(BuildProfileRequest(Chapters.Cebu, MemberTypes.Regular), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        var fetched = await controller.GetMyProfile(CancellationToken.None);
        var dto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(fetched.Result).Value);
        Assert.Equal(Chapters.Ncr, dto.Chapter);
    }

    [Fact]
    public async Task UpdateMyProfile_AfterSubmit_ChangedPrcLicenseNoWithoutReupload_ReturnsBadRequest()
    {
        var userId = await CreateSubmittedApplicantAsync(prcLicenseNo: "MP-1");
        var controller = CreateController(userId);

        var result = await controller.UpdateMyProfile(
            BuildProfileRequest(Chapters.Ncr, MemberTypes.Regular, prcLicenseNo: "MP-2", prcIdReuploaded: false), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    private async Task UploadFreshPrcIdAsync(Guid userId)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("fake-pdf-bytes"));
        var uploadResult = await _memberUploadService.UploadAsync(userId, UploadKind.PrcId, stream, "id.pdf", stream.Length, CancellationToken.None);
        Assert.True(uploadResult.Succeeded);
    }

    [Fact]
    public async Task UpdateMyProfile_AfterSubmit_ChangedPrcLicenseNoWithFreshUpload_StagesPendingValue()
    {
        var userId = await CreateSubmittedApplicantAsync(prcLicenseNo: "MP-1");
        var controller = CreateController(userId);
        await UploadFreshPrcIdAsync(userId);

        var result = await controller.UpdateMyProfile(
            BuildProfileRequest(Chapters.Ncr, MemberTypes.Regular, prcLicenseNo: "MP-2", prcIdReuploaded: true), CancellationToken.None);

        // The old value stays current until an admin approves - nothing is overwritten yet.
        var dto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("MP-1", dto.PrcLicenseNo);
        Assert.Equal("MP-2", dto.PendingPrcLicenseNo);
        Assert.False(dto.PrcIdVerified);
    }

    [Fact]
    public async Task ApprovePrcVerification_WithPendingChange_CopiesPendingIntoCurrentAndMarksVerified()
    {
        var userId = await CreateSubmittedApplicantAsync(prcLicenseNo: "MP-1");
        var memberId = (await _memberService.GetByUserIdAsync(userId))!.Id;
        var memberController = CreateController(userId);
        await UploadFreshPrcIdAsync(userId);
        await memberController.UpdateMyProfile(
            BuildProfileRequest(Chapters.Ncr, MemberTypes.Regular, prcLicenseNo: "MP-2", prcIdReuploaded: true), CancellationToken.None);

        var adminController = CreateController();
        var result = await adminController.ApprovePrcVerification(memberId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var fetched = await adminController.GetById(memberId, CancellationToken.None);
        var dto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(fetched.Result).Value);
        Assert.Equal("MP-2", dto.PrcLicenseNo);
        Assert.Null(dto.PendingPrcLicenseNo);
        Assert.True(dto.PrcIdVerified);
    }

    [Fact]
    public async Task RejectPrcVerification_WithPendingChange_DiscardsPendingAndSetsReason_LeavesCurrentValueUnchanged()
    {
        var userId = await CreateSubmittedApplicantAsync(prcLicenseNo: "MP-1");
        var memberId = (await _memberService.GetByUserIdAsync(userId))!.Id;
        var memberController = CreateController(userId);
        await UploadFreshPrcIdAsync(userId);
        await memberController.UpdateMyProfile(
            BuildProfileRequest(Chapters.Ncr, MemberTypes.Regular, prcLicenseNo: "MP-2", prcIdReuploaded: true), CancellationToken.None);

        var adminController = CreateController();
        var result = await adminController.RejectPrcVerification(memberId, new RejectPrcVerificationRequest("Document is illegible"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var fetched = await memberController.GetMyProfile(CancellationToken.None);
        var dto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(fetched.Result).Value);
        Assert.Equal("MP-1", dto.PrcLicenseNo);
        Assert.Null(dto.PendingPrcLicenseNo);
        Assert.Equal("Document is illegible", dto.PrcVerificationRejectedReason);
    }

    [Fact]
    public async Task GetAll_WithPendingPrcVerificationOnly_IncludesNeverVerifiedAndPendingChange_ExcludesVerified()
    {
        var neverVerifiedUserId = await CreateSubmittedApplicantAsync(prcLicenseNo: "MP-1");
        var pendingChangeUserId = await CreateSubmittedApplicantAsync(prcLicenseNo: "MP-3");
        var pendingChangeMemberId = (await _memberService.GetByUserIdAsync(pendingChangeUserId))!.Id;
        var pendingChangeController = CreateController(pendingChangeUserId);
        await UploadFreshPrcIdAsync(pendingChangeUserId);
        await pendingChangeController.UpdateMyProfile(
            BuildProfileRequest(Chapters.Ncr, MemberTypes.Regular, prcLicenseNo: "MP-4", prcIdReuploaded: true), CancellationToken.None);

        var adminController = CreateController();
        // A submitted member with no PRC License No. at all can no longer be created through *any*
        // path - the wizard has always required it, and CreateAsync now does too, precisely so
        // nobody can end up unverifiable and therefore unapprovable. Rows in that shape still exist
        // in older data though, so the filter must still exclude them: seeded straight into the
        // context here rather than through an API that would (correctly) refuse it.
        var noPrcUser = await CreateUserAsync();
        var db = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var noPrcMember = new Member
        {
            UserId = noPrcUser.Id,
            FirstName = "Legacy",
            LastName = "NoLicence",
            Chapter = Chapters.Ncr,
            MemberType = MemberTypes.Regular,
            PrcLicenseNo = null,
            Status = MembershipStatus.Pending,
            SubmittedAt = DateTimeOffset.UtcNow,
        };
        db.Members.Add(noPrcMember);
        await db.SaveChangesAsync();

        var result = await adminController.GetAll(page: 1, pageSize: 1000, pendingPrcVerificationOnly: true, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var paged = Assert.IsType<PagedResult<MemberDto>>(ok.Value);
        var neverVerifiedMemberId = (await _memberService.GetByUserIdAsync(neverVerifiedUserId))!.Id;
        Assert.Contains(paged.Items, m => m.Id == neverVerifiedMemberId);
        Assert.Contains(paged.Items, m => m.Id == pendingChangeMemberId);
        Assert.DoesNotContain(paged.Items, m => m.Id == noPrcMember.Id);
    }

    [Fact]
    public async Task RejectPrcVerification_ForNeverVerifiedMember_KeepsThemInTheQueue()
    {
        var userId = await CreateSubmittedApplicantAsync(prcLicenseNo: "MP-1");
        var memberId = (await _memberService.GetByUserIdAsync(userId))!.Id;
        var adminController = CreateController();

        await adminController.RejectPrcVerification(memberId, new RejectPrcVerificationRequest("Please resubmit"), CancellationToken.None);

        var result = await adminController.GetAll(page: 1, pageSize: 1000, pendingPrcVerificationOnly: true, cancellationToken: CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var paged = Assert.IsType<PagedResult<MemberDto>>(ok.Value);
        Assert.Contains(paged.Items, m => m.Id == memberId);
    }

    [Theory]
    [InlineData(RoleNames.SuperAdmin)]
    [InlineData(RoleNames.Admin)]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Accounts)]
    public async Task GetAll_ExcludesMemberRowOwnedByAdministrativeAccount_AndTotalCountReflectsIt(string administrativeRole)
    {
        var controller = CreateController();
        var baseline = await controller.GetAll(page: 1, pageSize: 1000, cancellationToken: CancellationToken.None);
        var baselineTotal = Assert.IsType<PagedResult<MemberDto>>(Assert.IsType<OkObjectResult>(baseline.Result).Value).TotalCount;

        var adminUser = await CreateUserAsync(administrativeRole);
        var created = await controller.Create(BuildCreateRequest(adminUser.Id), CancellationToken.None);
        var createdDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(created.Result).Value);

        var result = await controller.GetAll(page: 1, pageSize: 1000, cancellationToken: CancellationToken.None);
        var paged = Assert.IsType<PagedResult<MemberDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.DoesNotContain(paged.Items, m => m.Id == createdDto.Id);
        Assert.Equal(baselineTotal, paged.TotalCount);
    }

    [Theory]
    [InlineData(RoleNames.SuperAdmin)]
    [InlineData(RoleNames.Admin)]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Accounts)]
    public async Task GetById_MemberRowOwnedByAdministrativeAccount_ReturnsNotFound(string administrativeRole)
    {
        var controller = CreateController();
        var adminUser = await CreateUserAsync(administrativeRole);
        var created = await controller.Create(BuildCreateRequest(adminUser.Id), CancellationToken.None);
        var createdDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(created.Result).Value);

        var result = await controller.GetById(createdDto.Id, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Theory]
    [InlineData(RoleNames.SuperAdmin)]
    [InlineData(RoleNames.Admin)]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Accounts)]
    public async Task UpdateMyProfile_AdministrativeAccountWithNoExistingProfile_ReturnsForbidden_AndCreatesNoProfile(string administrativeRole)
    {
        var adminUser = await CreateUserAsync(administrativeRole);
        var controller = CreateController(adminUser.Id);
        var request = new UpdateMyProfileRequest(
            FirstName: "Staff", MiddleName: null, LastName: "Account", Suffix: null,
            Birthdate: null, Gender: null, CivilStatus: null,
            EducationLevel: null, SchoolName: null, CourseYearGraduated: null, SpecifiedProfession: null,
            MobileNumber: null,
            HouseNo: null, Street: null, Barangay: null, CityMunicipality: null, Province: null, ZipCode: null, Country: null,
            MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null, MailingCountry: null,
            HousePhone: null,
            PrcLicenseNo: null, PrcRegistrationDate: null, PrcValidUntilDate: null, PtrNumber: null, PtrPlaceIssued: null, PtrDateIssued: null, Tin: null, Chapter: Chapters.Ncr, ChapterYear: null, ChapterPosition: null,
            EmploymentStatus: null, Company: null, Position: null, BusinessAddress: null, YearsOfPractice: null, Specialization: null, Skills: null,
            MemberType: MemberTypes.Regular);

        var result = await controller.UpdateMyProfile(request, CancellationToken.None);

        // Asserts the status code rather than ForbidResult: the refusal now carries a message and
        // code so the client can explain it, where Forbid() wrote a zero-byte body and left the
        // user staring at a silent failure. The behaviour under test - refused, and no profile
        // created - is unchanged.
        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal(
            "ADMIN_ACCOUNT_NO_PROFILE",
            forbidden.Value?.GetType().GetProperty("code")?.GetValue(forbidden.Value) as string);
        Assert.Null(await _memberService.GetByUserIdAsync(adminUser.Id));
    }

    [Fact]
    public async Task GetMyProfileCompleteness_BeforeSubmit_ReturnsZeroPercent()
    {
        var user = await CreateUserAsync();
        var controller = CreateController(user.Id);
        await controller.UpdateMyProfile(BuildProfileRequest(Chapters.Ncr, MemberTypes.Regular), CancellationToken.None);

        var result = await controller.GetMyProfileCompleteness(CancellationToken.None);

        var dto = Assert.IsType<ProfileCompletenessDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.False(dto.IsSubmitted);
        Assert.Equal(0, dto.PercentComplete);
    }

    [Fact]
    public async Task GetMyProfileCompleteness_AfterSubmit_ReturnsBaselineFiftyPercent()
    {
        var userId = await CreateSubmittedApplicantAsync(prcLicenseNo: "MP-1");
        var controller = CreateController(userId);

        var result = await controller.GetMyProfileCompleteness(CancellationToken.None);

        var dto = Assert.IsType<ProfileCompletenessDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(dto.IsSubmitted);
        Assert.Equal(50, dto.PercentComplete);
        Assert.True(dto.HasPrcId);
    }

    [Fact]
    public async Task GetMyProfileCompleteness_NoProfileYet_ReturnsNotFound()
    {
        var user = await CreateUserAsync();
        var controller = CreateController(user.Id);

        var result = await controller.GetMyProfileCompleteness(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetMemberProfileCompleteness_AdminViewingSubmittedMember_ReturnsCompleteness()
    {
        var userId = await CreateSubmittedApplicantAsync(prcLicenseNo: "MP-1");
        var memberId = (await _memberService.GetByUserIdAsync(userId))!.Id;
        var adminController = CreateController();

        var result = await adminController.GetMemberProfileCompleteness(memberId, CancellationToken.None);

        var dto = Assert.IsType<ProfileCompletenessDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(dto.IsSubmitted);
        Assert.Equal(50, dto.PercentComplete);
    }

    [Fact]
    public async Task GetMemberProfileCompleteness_UnknownId_ReturnsNotFound()
    {
        var controller = CreateController();

        var result = await controller.GetMemberProfileCompleteness(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
