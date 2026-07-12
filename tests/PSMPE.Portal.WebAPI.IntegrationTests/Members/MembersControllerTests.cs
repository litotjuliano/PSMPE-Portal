using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Application.Members.Dtos;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.WebAPI.Controllers;
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

    public MembersControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _scope = factory.Services.CreateScope();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _memberService = _scope.ServiceProvider.GetRequiredService<IMemberService>();
        _memberUploadService = _scope.ServiceProvider.GetRequiredService<IMemberUploadService>();
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
        return new MembersController(_memberService, _memberUploadService, _userManager)
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
        Address: "1234 Main St, Quezon City",
        PrcLicenseNo: "MP 12345",
        Chapter: Chapters.QuezonCity,
        Company: "JLA Plumbing Works Inc.",
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
            Birthdate: null, Gender: "Female", Address: "Cebu City",
            PrcLicenseNo: null, Chapter: Chapters.Cebu, Company: null, MemberType: MemberTypes.Regular);

        var updateResult = await controller.UpdateMyProfile(request, CancellationToken.None);
        var updated = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(updateResult.Result).Value);
        Assert.Equal(MembershipStatus.Pending, updated.Status);
        Assert.False(string.IsNullOrWhiteSpace(updated.MembershipNo));

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
            Birthdate: null, Gender: null, Address: null,
            PrcLicenseNo: null, Chapter: Chapters.Davao, Company: null, MemberType: MemberTypes.Regular);

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
            Birthdate: createdDto.Birthdate, Gender: createdDto.Gender, Address: createdDto.Address,
            PrcLicenseNo: createdDto.PrcLicenseNo, Chapter: createdDto.Chapter, Company: createdDto.Company,
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
            "First", null, "Last", null, null, null, null, null, Chapters.Ncr, null,
            MemberTypes.Regular, MembershipStatus.Active, null, null);

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

        var firstApprove = await controller.Approve(createdDto.Id, CancellationToken.None);
        Assert.IsType<NoContentResult>(firstApprove);

        var afterFirst = await controller.GetById(createdDto.Id, CancellationToken.None);
        var afterFirstDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(afterFirst.Result).Value);
        Assert.NotNull(afterFirstDto.ApprovedAt);

        var secondApprove = await controller.Approve(createdDto.Id, CancellationToken.None);
        Assert.IsType<NoContentResult>(secondApprove);

        var afterSecond = await controller.GetById(createdDto.Id, CancellationToken.None);
        var afterSecondDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(afterSecond.Result).Value);
        Assert.Equal(afterFirstDto.ApprovedAt, afterSecondDto.ApprovedAt);
    }

    [Fact]
    public async Task Approve_UnknownId_ReturnsNotFound()
    {
        var controller = CreateController();

        var result = await controller.Approve(Guid.NewGuid(), CancellationToken.None);

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
            activeDto.FirstName, activeDto.MiddleName, activeDto.LastName, activeDto.Suffix,
            activeDto.Birthdate, activeDto.Gender, activeDto.Address, activeDto.PrcLicenseNo,
            activeDto.Chapter, activeDto.Company, activeDto.MemberType, MembershipStatus.Active,
            activeDto.RenewalDueDate, activeDto.NationalDuesReferenceNo);
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
        await controller.Approve(approvedDto.Id, CancellationToken.None);

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
            createdDto.FirstName, createdDto.MiddleName, createdDto.LastName, createdDto.Suffix,
            createdDto.Birthdate, createdDto.Gender, createdDto.Address, createdDto.PrcLicenseNo,
            createdDto.Chapter, createdDto.Company, createdDto.MemberType, MembershipStatus.Active,
            dueDate, createdDto.NationalDuesReferenceNo);
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
            Birthdate: null, Gender: null, Address: null,
            PrcLicenseNo: null, Chapter: "", Company: null, MemberType: "");
        await controller.UpdateMyProfile(request, CancellationToken.None);

        var result = await controller.SubmitMyProfile(CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SubmitMyProfile_WithRequiredFieldsFilled_SetsSubmittedAt_AndIsIdempotent()
    {
        var user = await CreateUserAsync();
        var controller = CreateController(user.Id);
        var request = new UpdateMyProfileRequest(
            FirstName: "Draft", MiddleName: null, LastName: "Applicant", Suffix: null,
            Birthdate: null, Gender: null, Address: null,
            PrcLicenseNo: null, Chapter: Chapters.Ncr, Company: null, MemberType: MemberTypes.Regular);
        await controller.UpdateMyProfile(request, CancellationToken.None);

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
            Birthdate: null, Gender: null, Address: null,
            PrcLicenseNo: null, Chapter: Chapters.Ncr, Company: null, MemberType: MemberTypes.Regular);
        var draftResult = await draftController.UpdateMyProfile(draftRequest, CancellationToken.None);
        var draftDto = Assert.IsType<MemberDto>(Assert.IsType<OkObjectResult>(draftResult.Result).Value);
        Assert.Null(draftDto.SubmittedAt);

        var result = await adminController.GetAll(page: 1, pageSize: 1000, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var paged = Assert.IsType<PagedResult<MemberDto>>(ok.Value);
        Assert.DoesNotContain(paged.Items, m => m.Id == draftDto.Id);
    }

    private static UpdateMyProfileRequest BuildProfileRequest(
        string chapter, string memberType, string? prcLicenseNo = null, bool prcIdReuploaded = false) => new(
        FirstName: "Juan", MiddleName: null, LastName: "Dela Cruz", Suffix: null,
        Birthdate: null, Gender: null, Address: null,
        PrcLicenseNo: prcLicenseNo, Chapter: chapter, Company: null, MemberType: memberType,
        PrcIdReuploaded: prcIdReuploaded);

    private async Task<Guid> CreateSubmittedApplicantAsync(string? prcLicenseNo = null)
    {
        var user = await CreateUserAsync();
        var controller = CreateController(user.Id);
        await controller.UpdateMyProfile(BuildProfileRequest(Chapters.Ncr, MemberTypes.Regular, prcLicenseNo), CancellationToken.None);
        await controller.SubmitMyProfile(CancellationToken.None);
        return user.Id;
    }

    [Fact]
    public async Task UpdateMyProfile_AfterSubmit_CraftedMemberTypeOrChapterChange_ReturnsBadRequest()
    {
        var userId = await CreateSubmittedApplicantAsync();
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
        var noPrcUserId = await CreateSubmittedApplicantAsync();
        var pendingChangeMemberId = (await _memberService.GetByUserIdAsync(pendingChangeUserId))!.Id;
        var pendingChangeController = CreateController(pendingChangeUserId);
        await UploadFreshPrcIdAsync(pendingChangeUserId);
        await pendingChangeController.UpdateMyProfile(
            BuildProfileRequest(Chapters.Ncr, MemberTypes.Regular, prcLicenseNo: "MP-4", prcIdReuploaded: true), CancellationToken.None);

        var adminController = CreateController();
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
            Birthdate: null, Gender: null, Address: null,
            PrcLicenseNo: null, Chapter: Chapters.Ncr, Company: null, MemberType: MemberTypes.Regular);

        var result = await controller.UpdateMyProfile(request, CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Null(await _memberService.GetByUserIdAsync(adminUser.Id));
    }
}
