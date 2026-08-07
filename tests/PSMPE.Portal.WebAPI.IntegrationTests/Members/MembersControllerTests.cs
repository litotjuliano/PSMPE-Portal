using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Application.Members.Dtos;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
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

    public MembersControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _scope = factory.Services.CreateScope();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _memberService = _scope.ServiceProvider.GetRequiredService<IMemberService>();
        _memberUploadService = _scope.ServiceProvider.GetRequiredService<IMemberUploadService>();
        _memberCertificateService = _scope.ServiceProvider.GetRequiredService<IMemberCertificateService>();
        _emailSender = _scope.ServiceProvider.GetRequiredService<IEmailSender>();
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
        return new MembersController(_memberService, _memberUploadService, _memberCertificateService, _userManager, _emailSender)
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
        ZipCode: "1100",
        MailingHouseNo: null,
        MailingStreet: null,
        MailingBarangay: null,
        MailingCityMunicipality: null,
        MailingProvince: null,
        MailingZipCode: null,
        HousePhone: null,
        Website: null,
        FacebookUrl: null,
        LinkedInUrl: null,
        XUrl: null,
        InstagramUrl: null,
        PrcLicenseNo: "MP 12345",
        PrcRegistrationDate: new DateOnly(2020, 1, 1),
        PrcValidUntilDate: new DateOnly(2030, 1, 1),
        PtrNumber: "PTR-0012345",
        Tin: null,
        Chapter: Chapters.QuezonCity,
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
            HouseNo: dto.HouseNo, Street: dto.Street, Barangay: dto.Barangay, CityMunicipality: dto.CityMunicipality, Province: dto.Province, ZipCode: dto.ZipCode,
            MailingHouseNo: dto.MailingHouseNo, MailingStreet: dto.MailingStreet, MailingBarangay: dto.MailingBarangay,
            MailingCityMunicipality: dto.MailingCityMunicipality, MailingProvince: dto.MailingProvince, MailingZipCode: dto.MailingZipCode,
            HousePhone: null, Website: null, FacebookUrl: null, LinkedInUrl: null, XUrl: null, InstagramUrl: null,
            PrcLicenseNo: dto.PrcLicenseNo, PrcRegistrationDate: dto.PrcRegistrationDate, PrcValidUntilDate: dto.PrcValidUntilDate,
            PtrNumber: "PTR-9999999", Tin: "123-456-789",
            Chapter: dto.Chapter, EmploymentStatus: null, Company: dto.Company, Position: null, BusinessAddress: null,
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
            HouseNo: null, Street: "Cebu City", Barangay: null, CityMunicipality: null, Province: null, ZipCode: null,
            MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null,
            HousePhone: null, Website: null, FacebookUrl: null, LinkedInUrl: null, XUrl: null, InstagramUrl: null,
            PrcLicenseNo: null, PrcRegistrationDate: null, PrcValidUntilDate: null, PtrNumber: null, Tin: null, Chapter: Chapters.Cebu,
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
            HouseNo: null, Street: null, Barangay: null, CityMunicipality: null, Province: null, ZipCode: null,
            MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null,
            HousePhone: null, Website: null, FacebookUrl: null, LinkedInUrl: null, XUrl: null, InstagramUrl: null,
            PrcLicenseNo: null, PrcRegistrationDate: null, PrcValidUntilDate: null, PtrNumber: null, Tin: null, Chapter: Chapters.Davao,
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
            HouseNo: createdDto.HouseNo, Street: createdDto.Street, Barangay: createdDto.Barangay, CityMunicipality: createdDto.CityMunicipality, Province: createdDto.Province, ZipCode: createdDto.ZipCode,
            MailingHouseNo: createdDto.MailingHouseNo, MailingStreet: createdDto.MailingStreet, MailingBarangay: createdDto.MailingBarangay,
            MailingCityMunicipality: createdDto.MailingCityMunicipality, MailingProvince: createdDto.MailingProvince, MailingZipCode: createdDto.MailingZipCode,
            HousePhone: null, Website: null, FacebookUrl: null, LinkedInUrl: null, XUrl: null, InstagramUrl: null,
            PrcLicenseNo: createdDto.PrcLicenseNo, PrcRegistrationDate: createdDto.PrcRegistrationDate, PrcValidUntilDate: createdDto.PrcValidUntilDate,
            PtrNumber: createdDto.PtrNumber, Tin: createdDto.Tin,
            Chapter: createdDto.Chapter,
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
            HouseNo: null, Street: null, Barangay: null, CityMunicipality: null, Province: null, ZipCode: null,
            MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null,
            HousePhone: null, Website: null, FacebookUrl: null, LinkedInUrl: null, XUrl: null, InstagramUrl: null,
            PrcLicenseNo: null, PrcRegistrationDate: null, PrcValidUntilDate: null, PtrNumber: null, Tin: null, Chapter: Chapters.Ncr,
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

        var firstApprove = await controller.Approve(createdDto.Id, new ApproveMemberRequest("A-0001"), CancellationToken.None);
        Assert.IsType<NoContentResult>(firstApprove);

        var afterFirst = await controller.GetById(createdDto.Id, CancellationToken.None);
        var afterFirstDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(afterFirst.Result).Value);
        Assert.NotNull(afterFirstDto.ApprovedAt);

        var secondApprove = await controller.Approve(createdDto.Id, new ApproveMemberRequest("A-0002"), CancellationToken.None);
        Assert.IsType<NoContentResult>(secondApprove);

        var afterSecond = await controller.GetById(createdDto.Id, CancellationToken.None);
        var afterSecondDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(afterSecond.Result).Value);
        Assert.Equal(afterFirstDto.ApprovedAt, afterSecondDto.ApprovedAt);
        // The second call passed a different number on purpose: a repeat approval must not
        // renumber a live member.
        Assert.Equal("A-0001", afterSecondDto.MembershipNo);
    }

    [Fact]
    public async Task Approve_AssignsTheMembershipNoFromTheRequest()
    {
        var user = await CreateUserAsync();
        var controller = CreateController();
        var created = await controller.Create(BuildCreateRequest(user.Id), CancellationToken.None);
        var createdDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(created.Result).Value);

        // Padded so the trim is exercised - admins paste these out of spreadsheets.
        var result = await controller.Approve(createdDto.Id, new ApproveMemberRequest("  PSMPE-2026-000123  "), CancellationToken.None);
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

        var result = await controller.Approve(createdDto.Id, new ApproveMemberRequest(membershipNo), CancellationToken.None);

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
        Assert.IsType<NoContentResult>(
            await controller.Approve(firstDto.Id, new ApproveMemberRequest("DUPLICATE-1"), CancellationToken.None));

        var secondUser = await CreateUserAsync();
        var secondCreated = await controller.Create(BuildCreateRequest(secondUser.Id), CancellationToken.None);
        var secondDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(secondCreated.Result).Value);

        var result = await controller.Approve(secondDto.Id, new ApproveMemberRequest("DUPLICATE-1"), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        var fetched = await controller.GetById(secondDto.Id, CancellationToken.None);
        var dto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(fetched.Result).Value);
        Assert.Null(dto.ApprovedAt);
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

        var approveResult = await adminController.Approve(createdDto.Id, new ApproveMemberRequest("A-0003"), CancellationToken.None);
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

        Assert.IsType<NoContentResult>(await adminController.Approve(createdDto.Id, new ApproveMemberRequest(Guid.NewGuid().ToString("N")[..8]), CancellationToken.None));
        Assert.IsType<NoContentResult>(await adminController.Approve(createdDto.Id, new ApproveMemberRequest(Guid.NewGuid().ToString("N")[..8]), CancellationToken.None));

        var afterSecondApprove = await CreateController(user.Id).GetMyReceipt(CancellationToken.None);
        Assert.IsType<FileStreamResult>(afterSecondApprove);
    }

    [Fact]
    public async Task Approve_UnknownId_ReturnsNotFound()
    {
        var controller = CreateController();

        var result = await controller.Approve(Guid.NewGuid(), new ApproveMemberRequest("A-0006"), CancellationToken.None);

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
            HouseNo: activeDto.HouseNo, Street: activeDto.Street, Barangay: activeDto.Barangay, CityMunicipality: activeDto.CityMunicipality, Province: activeDto.Province, ZipCode: activeDto.ZipCode,
            MailingHouseNo: activeDto.MailingHouseNo, MailingStreet: activeDto.MailingStreet, MailingBarangay: activeDto.MailingBarangay,
            MailingCityMunicipality: activeDto.MailingCityMunicipality, MailingProvince: activeDto.MailingProvince, MailingZipCode: activeDto.MailingZipCode,
            HousePhone: activeDto.HousePhone, Website: activeDto.Website, FacebookUrl: activeDto.FacebookUrl, LinkedInUrl: activeDto.LinkedInUrl, XUrl: activeDto.XUrl, InstagramUrl: activeDto.InstagramUrl,
            PrcLicenseNo: activeDto.PrcLicenseNo, PrcRegistrationDate: activeDto.PrcRegistrationDate, PrcValidUntilDate: activeDto.PrcValidUntilDate,
            PtrNumber: activeDto.PtrNumber, Tin: activeDto.Tin,
            Chapter: activeDto.Chapter, EmploymentStatus: activeDto.EmploymentStatus, Company: activeDto.Company, Position: activeDto.Position, BusinessAddress: activeDto.BusinessAddress,
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
        await controller.Approve(approvedDto.Id, new ApproveMemberRequest("A-0007"), CancellationToken.None);

        var result = await controller.GetAll(page: 1, pageSize: 1000, pendingApprovalOnly: true, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var paged = Assert.IsType<PagedResult<MemberDto>>(ok.Value);
        Assert.Contains(paged.Items, m => m.Id == unapprovedDto.Id);
        Assert.DoesNotContain(paged.Items, m => m.Id == approvedDto.Id);
    }

    [Theory]
    [InlineData(-10, true)]
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
            HouseNo: createdDto.HouseNo, Street: createdDto.Street, Barangay: createdDto.Barangay, CityMunicipality: createdDto.CityMunicipality, Province: createdDto.Province, ZipCode: createdDto.ZipCode,
            MailingHouseNo: createdDto.MailingHouseNo, MailingStreet: createdDto.MailingStreet, MailingBarangay: createdDto.MailingBarangay,
            MailingCityMunicipality: createdDto.MailingCityMunicipality, MailingProvince: createdDto.MailingProvince, MailingZipCode: createdDto.MailingZipCode,
            HousePhone: createdDto.HousePhone, Website: createdDto.Website, FacebookUrl: createdDto.FacebookUrl, LinkedInUrl: createdDto.LinkedInUrl, XUrl: createdDto.XUrl, InstagramUrl: createdDto.InstagramUrl,
            PrcLicenseNo: createdDto.PrcLicenseNo, PrcRegistrationDate: createdDto.PrcRegistrationDate, PrcValidUntilDate: createdDto.PrcValidUntilDate,
            PtrNumber: createdDto.PtrNumber, Tin: createdDto.Tin,
            Chapter: createdDto.Chapter, EmploymentStatus: createdDto.EmploymentStatus, Company: createdDto.Company, Position: createdDto.Position, BusinessAddress: createdDto.BusinessAddress,
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
            HouseNo: null, Street: null, Barangay: null, CityMunicipality: null, Province: null, ZipCode: null,
            MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null,
            HousePhone: null, Website: null, FacebookUrl: null, LinkedInUrl: null, XUrl: null, InstagramUrl: null,
            PrcLicenseNo: null, PrcRegistrationDate: null, PrcValidUntilDate: null, PtrNumber: null, Tin: null, Chapter: "",
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
        HouseNo: null, Street: "123 Sample St", Barangay: "Sample Barangay", CityMunicipality: "Sample City", Province: "Sample Province", ZipCode: "1000",
        MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null,
        HousePhone: null, Website: null, FacebookUrl: null, LinkedInUrl: null, XUrl: null, InstagramUrl: null,
        PrcLicenseNo: "MP 99999", PrcRegistrationDate: new DateOnly(2020, 1, 1), PrcValidUntilDate: new DateOnly(2030, 1, 1),
        PtrNumber: "PTR-0099999", Tin: null,
        Chapter: Chapters.Ncr,
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
    [InlineData("ptrNumber")]
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
            "ptrNumber" => complete with { PtrNumber = null },
            _ => throw new ArgumentOutOfRangeException(nameof(fieldToOmit)),
        };
        await controller.UpdateMyProfile(request, CancellationToken.None);
        await UploadFreshPrcIdAsync(user.Id);
        await UploadPhotoAsync(user.Id);
        await UploadProofOfPaymentAsync(user.Id);

        var result = await controller.SubmitMyProfile(CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
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
            HouseNo: null, Street: null, Barangay: null, CityMunicipality: null, Province: null, ZipCode: null,
            MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null,
            HousePhone: null, Website: null, FacebookUrl: null, LinkedInUrl: null, XUrl: null, InstagramUrl: null,
            PrcLicenseNo: null, PrcRegistrationDate: null, PrcValidUntilDate: null, PtrNumber: null, Tin: null, Chapter: Chapters.Ncr,
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
        HouseNo: null, Street: "123 Main St", Barangay: "Sample Barangay", CityMunicipality: "Sample City", Province: "Sample Province", ZipCode: "1000",
        MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null,
        HousePhone: null, Website: null, FacebookUrl: null, LinkedInUrl: null, XUrl: null, InstagramUrl: null,
        PrcLicenseNo: prcLicenseNo, PrcRegistrationDate: prcRegistrationDate ?? new DateOnly(2020, 1, 1), PrcValidUntilDate: prcValidUntilDate ?? new DateOnly(2030, 1, 1),
        PtrNumber: "PTR-0012345", Tin: null,
        Chapter: chapter,
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
        // PRC License No. is required for new self-service submissions, so "a submitted member
        // with none at all" can no longer arise from the wizard - constructed here via the
        // admin Create path instead (not subject to that self-service-only requirement) to keep
        // covering the filter's exclusion of members with nothing to verify.
        var noPrcUser = await CreateUserAsync();
        var noPrcCreated = await adminController.Create(BuildCreateRequest(noPrcUser.Id) with { PrcLicenseNo = null }, CancellationToken.None);
        var noPrcUserId = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(noPrcCreated.Result).Value).UserId;

        var result = await adminController.GetAll(page: 1, pageSize: 1000, pendingPrcVerificationOnly: true, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var paged = Assert.IsType<PagedResult<MemberDto>>(ok.Value);
        var neverVerifiedMemberId = (await _memberService.GetByUserIdAsync(neverVerifiedUserId))!.Id;
        var noPrcMemberId = (await _memberService.GetByUserIdAsync(noPrcUserId))!.Id;
        Assert.Contains(paged.Items, m => m.Id == neverVerifiedMemberId);
        Assert.Contains(paged.Items, m => m.Id == pendingChangeMemberId);
        Assert.DoesNotContain(paged.Items, m => m.Id == noPrcMemberId);
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
            HouseNo: null, Street: null, Barangay: null, CityMunicipality: null, Province: null, ZipCode: null,
            MailingHouseNo: null, MailingStreet: null, MailingBarangay: null, MailingCityMunicipality: null, MailingProvince: null, MailingZipCode: null,
            HousePhone: null, Website: null, FacebookUrl: null, LinkedInUrl: null, XUrl: null, InstagramUrl: null,
            PrcLicenseNo: null, PrcRegistrationDate: null, PrcValidUntilDate: null, PtrNumber: null, Tin: null, Chapter: Chapters.Ncr,
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
