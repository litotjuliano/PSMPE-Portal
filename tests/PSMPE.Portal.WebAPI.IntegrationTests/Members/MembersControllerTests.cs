using System.Security.Claims;
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

    public MembersControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _scope = factory.Services.CreateScope();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _memberService = _scope.ServiceProvider.GetRequiredService<IMemberService>();
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
        return new MembersController(_memberService, _userManager)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private async Task<ApplicationUser> CreateUserAsync()
    {
        var user = new ApplicationUser
        {
            UserName = $"{Guid.NewGuid()}@example.com",
            Email = $"{Guid.NewGuid()}@example.com",
            DisplayName = "Test User"
        };
        await _userManager.CreateAsync(user, "Password123!");
        await _userManager.AddToRoleAsync(user, RoleNames.Member);
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
            PrcLicenseNo: null, Chapter: Chapters.Cebu, Company: null);

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
            PrcLicenseNo: null, Chapter: Chapters.Davao, Company: null);

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
            Status: MembershipStatus.Active, RenewalDueDate: createdDto.RenewalDueDate,
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
            MembershipStatus.Active, null, null);

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
}
