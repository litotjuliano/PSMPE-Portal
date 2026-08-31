using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Application.Members.Dtos;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence.Seed;
using PSMPE.Portal.WebAPI.Controllers;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Admin;

/// <summary>
/// Exercises AdminController directly against the real UserManager/RoleManager (backed by the
/// InMemory database from CustomWebApplicationFactory), bypassing the HTTP/auth pipeline -
/// same convention as AiControllerTests. Roles and their default permissions are already seeded
/// by CustomWebApplicationFactory.InitializeAsync via the real IdentitySeeder.
///
/// CreateController(...) gives each test control over the calling user's roles/id via a
/// ControllerContext + ClaimsPrincipal, since AdminController.GetUsers/CreateUser/UpdateUser/
/// DeleteUser now read `User` directly (ControllerBase.User is null unless a ControllerContext
/// is set - direct instantiation alone, as the older tests in this file relied on, doesn't
/// provide one). _controller defaults to a Super Admin caller to preserve those older tests'
/// original "unrestricted" assumption.
/// </summary>
public class AdminControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly AdminController _controller;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly IMemberService _memberService;
    private readonly IMemberUploadService _memberUploadService;
    private readonly IMemberCertificateService _memberCertificateService;
    private readonly IEmailSender _emailSender;
    private readonly IConfiguration _configuration;

    public AdminControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _scope = factory.Services.CreateScope();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _roleManager = _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        _memberService = _scope.ServiceProvider.GetRequiredService<IMemberService>();
        _memberUploadService = _scope.ServiceProvider.GetRequiredService<IMemberUploadService>();
        _memberCertificateService = _scope.ServiceProvider.GetRequiredService<IMemberCertificateService>();
        _emailSender = _scope.ServiceProvider.GetRequiredService<IEmailSender>();
        _configuration = _scope.ServiceProvider.GetRequiredService<IConfiguration>();
        _controller = CreateController(callerRoles: RoleNames.SuperAdmin);
    }

    public Task InitializeAsync() => _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        _scope.Dispose();
        return Task.CompletedTask;
    }

    private AdminController CreateController(Guid? callerId = null, params string[] callerRoles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, (callerId ?? Guid.NewGuid()).ToString()) };
        claims.AddRange(callerRoles.Select(r => new Claim(ClaimTypes.Role, r)));
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) };
        return new AdminController(
            _userManager, _roleManager, NullLogger<AdminController>.Instance,
            _memberService, _memberUploadService, _memberCertificateService, _emailSender, _configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private async Task<ApplicationUser> CreateUserAsync(string role, string? displayName = null)
    {
        var user = new ApplicationUser
        {
            UserName = $"{Guid.NewGuid()}@example.com",
            Email = $"{Guid.NewGuid()}@example.com",
            DisplayName = displayName ?? "Test User"
        };
        await _userManager.CreateAsync(user, "Password123!");
        await _userManager.AddToRoleAsync(user, role);
        return user;
    }

    private static PagedResult<AdminController.UserSummaryDto> UnwrapPaged(ActionResult<PagedResult<AdminController.UserSummaryDto>> result) =>
        Assert.IsType<PagedResult<AdminController.UserSummaryDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);

    [Fact]
    public async Task GetUsers_ReturnsCreatedUserWithAssignedRole()
    {
        var user = await CreateUserAsync(RoleNames.Manager);

        var result = await _controller.GetUsers(page: 1, pageSize: 1000, cancellationToken: CancellationToken.None);

        var paged = UnwrapPaged(result);
        var summary = Assert.Single(paged.Items, s => s.Id == user.Id);
        Assert.Contains(RoleNames.Manager, summary.Roles);
    }

    [Fact]
    public async Task GetUsers_ExcludesSuperAdmin_WhenCallerIsNotSuperAdmin()
    {
        var superAdmin = await CreateUserAsync(RoleNames.SuperAdmin);
        var controller = CreateController(callerRoles: RoleNames.Admin);

        var result = await controller.GetUsers(page: 1, pageSize: 1000, cancellationToken: CancellationToken.None);

        var paged = UnwrapPaged(result);
        Assert.DoesNotContain(paged.Items, s => s.Id == superAdmin.Id);
    }

    [Fact]
    public async Task GetUsers_ExcludesOtherSuperAdmins_EvenWhenCallerIsSuperAdmin()
    {
        var otherSuperAdmin = await CreateUserAsync(RoleNames.SuperAdmin);

        var result = await _controller.GetUsers(page: 1, pageSize: 1000, cancellationToken: CancellationToken.None);

        var paged = UnwrapPaged(result);
        Assert.DoesNotContain(paged.Items, s => s.Id == otherSuperAdmin.Id);
    }

    [Fact]
    public async Task GetUsers_IncludesCallersOwnRow_WhenCallerIsSuperAdmin()
    {
        var superAdmin = await CreateUserAsync(RoleNames.SuperAdmin);
        var controller = CreateController(callerId: superAdmin.Id, callerRoles: RoleNames.SuperAdmin);

        var result = await controller.GetUsers(page: 1, pageSize: 1000, cancellationToken: CancellationToken.None);

        var paged = UnwrapPaged(result);
        Assert.Contains(paged.Items, s => s.Id == superAdmin.Id);
    }

    [Fact]
    public async Task GetUsers_RespectsSorting()
    {
        var first = await CreateUserAsync(RoleNames.Member, "AAA-Sort-First");
        var second = await CreateUserAsync(RoleNames.Member, "ZZZ-Sort-Second");

        var asc = UnwrapPaged(await _controller.GetUsers(page: 1, pageSize: 1000, sortBy: "displayName", sortDir: "asc"));
        var ascItems = asc.Items.ToList();
        Assert.True(ascItems.FindIndex(u => u.Id == first.Id) < ascItems.FindIndex(u => u.Id == second.Id));

        var desc = UnwrapPaged(await _controller.GetUsers(page: 1, pageSize: 1000, sortBy: "displayName", sortDir: "desc"));
        var descItems = desc.Items.ToList();
        Assert.True(descItems.FindIndex(u => u.Id == second.Id) < descItems.FindIndex(u => u.Id == first.Id));
    }

    [Fact]
    public async Task GetUsers_SortsByEmailConfirmed_ThenByCreatedAt()
    {
        var unverified = await CreateUserAsync(RoleNames.Member, "Unverified-User");
        var verified = await CreateUserAsync(RoleNames.Member, "Verified-User");
        await _userManager.ConfirmEmailAsync(verified, await _userManager.GenerateEmailConfirmationTokenAsync(verified));

        var asc = UnwrapPaged(await _controller.GetUsers(page: 1, pageSize: 1000, sortBy: "emailConfirmed", sortDir: "asc"));
        var ascItems = asc.Items.ToList();
        Assert.True(ascItems.FindIndex(u => u.Id == unverified.Id) < ascItems.FindIndex(u => u.Id == verified.Id));

        var desc = UnwrapPaged(await _controller.GetUsers(page: 1, pageSize: 1000, sortBy: "emailConfirmed", sortDir: "desc"));
        var descItems = desc.Items.ToList();
        Assert.True(descItems.FindIndex(u => u.Id == verified.Id) < descItems.FindIndex(u => u.Id == unverified.Id));
    }

    [Fact]
    public async Task GetUsers_RespectsPaging()
    {
        for (var i = 0; i < 3; i++)
        {
            await CreateUserAsync(RoleNames.Member, $"Paging-Test-{i}");
        }

        var fullCount = UnwrapPaged(await _controller.GetUsers(page: 1, pageSize: 1000)).TotalCount;
        var smallPage = UnwrapPaged(await _controller.GetUsers(page: 1, pageSize: 2));

        Assert.Equal(2, smallPage.Items.Count);
        Assert.Equal(fullCount, smallPage.TotalCount);
        Assert.Equal(1, smallPage.Page);
        Assert.Equal(2, smallPage.PageSize);
    }

    [Fact]
    public async Task GetUsers_WithSearch_MatchesDisplayNameCaseInsensitively()
    {
        var match = await CreateUserAsync(RoleNames.Manager, displayName: "Search Target Alpha");
        await CreateUserAsync(RoleNames.Manager, displayName: "Unrelated Beta");

        var result = UnwrapPaged(await _controller.GetUsers(
            page: 1, pageSize: 1000, search: "search target", cancellationToken: CancellationToken.None));

        Assert.Contains(result.Items, u => u.Id == match.Id);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task GetUsers_WithASingleRoleFilter_ReturnsOnlyThatRole()
    {
        var manager = await CreateUserAsync(RoleNames.Manager, displayName: "Single Filter Manager");
        var member = await CreateUserAsync(RoleNames.Member, displayName: "Single Filter Member");

        var result = UnwrapPaged(await _controller.GetUsers(
            page: 1, pageSize: 1000, roles: [RoleNames.Manager], cancellationToken: CancellationToken.None));

        Assert.Contains(result.Items, u => u.Id == manager.Id);
        Assert.DoesNotContain(result.Items, u => u.Id == member.Id);
    }

    /// <summary>
    /// Selecting two roles means "holds both", so the list narrows as chips are added rather than
    /// widening. This replaced a union - with two chips lit and a union behind them, the filter
    /// looked like it was ignoring one of them.
    /// </summary>
    [Fact]
    public async Task GetUsers_WithSeveralRolesFilter_RequiresAllOfThem()
    {
        var managerOnly = await CreateUserAsync(RoleNames.Manager, displayName: "Multi Filter Manager");
        var accountsOnly = await CreateUserAsync(RoleNames.Accounts, displayName: "Multi Filter Accounts");
        var both = await CreateUserAsync(RoleNames.Manager, displayName: "Multi Filter Both");
        await _controller.AssignRole(both.Id, new AdminController.AssignRoleRequest(RoleNames.Accounts));

        var result = UnwrapPaged(await _controller.GetUsers(
            page: 1, pageSize: 1000, roles: [RoleNames.Manager, RoleNames.Accounts], cancellationToken: CancellationToken.None));

        Assert.Contains(result.Items, u => u.Id == both.Id);
        Assert.DoesNotContain(result.Items, u => u.Id == managerOnly.Id);
        Assert.DoesNotContain(result.Items, u => u.Id == accountsOnly.Id);
    }

    [Fact]
    public async Task GetUserById_ReturnsUser()
    {
        var user = await CreateUserAsync(RoleNames.Manager);

        var result = await _controller.GetUserById(user.Id);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<AdminController.UserSummaryDto>(ok.Value);
        Assert.Equal(user.Id, summary.Id);
    }

    [Fact]
    public async Task GetUserById_TargetingSuperAdmin_AsAdmin_ReturnsNotFound()
    {
        var superAdmin = await CreateUserAsync(RoleNames.SuperAdmin);
        var controller = CreateController(callerRoles: RoleNames.Admin);

        var result = await controller.GetUserById(superAdmin.Id);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task CreateUser_AssignsRequestedRole()
    {
        var request = new AdminController.CreateUserRequest($"{Guid.NewGuid()}@example.com", "New Manager", "Password123!", RoleNames.Manager);

        var result = await _controller.CreateUser(request);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<AdminController.UserSummaryDto>(ok.Value);
        Assert.Contains(RoleNames.Manager, summary.Roles);
    }

    [Fact]
    public async Task CreateUser_NonSuperAdminRequestingNonMemberRole_ReturnsForbidden()
    {
        var controller = CreateController(callerRoles: RoleNames.Admin);
        var request = new AdminController.CreateUserRequest($"{Guid.NewGuid()}@example.com", "New Manager", "Password123!", RoleNames.Manager);

        var result = await controller.CreateUser(request);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task CreateUser_NonSuperAdminRequestingMemberRole_Succeeds()
    {
        var controller = CreateController(callerRoles: RoleNames.Admin);
        var request = new AdminController.CreateUserRequest($"{Guid.NewGuid()}@example.com", "New Member", "Password123!", RoleNames.Member);

        var result = await controller.CreateUser(request);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<AdminController.UserSummaryDto>(ok.Value);
        Assert.Contains(RoleNames.Member, summary.Roles);
    }

    [Fact]
    public async Task CreateUser_DuplicateEmail_ReturnsConflict()
    {
        var existing = await CreateUserAsync(RoleNames.Member);
        var request = new AdminController.CreateUserRequest(existing.Email!, "Duplicate", "Password123!", null);

        var result = await _controller.CreateUser(request);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateUser_ChangesDisplayNameAndEmail()
    {
        var user = await CreateUserAsync(RoleNames.Member);
        var newEmail = $"{Guid.NewGuid()}@example.com";

        var result = await _controller.UpdateUser(user.Id, new AdminController.UpdateUserRequest("Updated Name", newEmail, null));

        Assert.IsType<NoContentResult>(result);
        var updated = await _userManager.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated!.DisplayName);
        Assert.Equal(newEmail, updated.Email);
    }

    [Fact]
    public async Task UpdateUser_WithNewPassword_AllowsLoginWithNewPassword()
    {
        var user = await CreateUserAsync(RoleNames.Member);

        var result = await _controller.UpdateUser(user.Id, new AdminController.UpdateUserRequest(user.DisplayName, user.Email!, "NewPassword456!"));

        Assert.IsType<NoContentResult>(result);
        Assert.True(await _userManager.CheckPasswordAsync(user, "NewPassword456!"));
    }

    [Fact]
    public async Task UpdateUser_TargetingSuperAdmin_AsAdmin_ReturnsNotFound()
    {
        var superAdmin = await CreateUserAsync(RoleNames.SuperAdmin);
        var controller = CreateController(callerRoles: RoleNames.Admin);

        var result = await controller.UpdateUser(superAdmin.Id, new AdminController.UpdateUserRequest("Hacked Name", superAdmin.Email!, null));

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task UpdateUser_TargetingSelfAsSuperAdmin_ReturnsForbidden()
    {
        var superAdmin = await CreateUserAsync(RoleNames.SuperAdmin);
        var controller = CreateController(callerId: superAdmin.Id, callerRoles: RoleNames.SuperAdmin);

        var result = await controller.UpdateUser(superAdmin.Id, new AdminController.UpdateUserRequest("New Name", superAdmin.Email!, null));

        Assert.IsType<ForbidResult>(result);
        var unchanged = await _userManager.FindByIdAsync(superAdmin.Id.ToString());
        Assert.Equal(superAdmin.DisplayName, unchanged!.DisplayName);
    }

    [Fact]
    public async Task DeleteUser_RemovesUser()
    {
        var user = await CreateUserAsync(RoleNames.Member);

        var result = await _controller.DeleteUser(user.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await _userManager.FindByIdAsync(user.Id.ToString()));
    }

    [Fact]
    public async Task DeleteUser_Self_ReturnsBadRequest()
    {
        var user = await CreateUserAsync(RoleNames.Member);
        var controller = CreateController(user.Id);

        var result = await controller.DeleteUser(user.Id);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(await _userManager.FindByIdAsync(user.Id.ToString()));
    }

    [Fact]
    public async Task DeleteUser_TargetingSelfAsSuperAdmin_ReturnsForbidden()
    {
        var superAdmin = await CreateUserAsync(RoleNames.SuperAdmin);
        var controller = CreateController(callerId: superAdmin.Id, callerRoles: RoleNames.SuperAdmin);

        var result = await controller.DeleteUser(superAdmin.Id);

        Assert.IsType<ForbidResult>(result);
        Assert.NotNull(await _userManager.FindByIdAsync(superAdmin.Id.ToString()));
    }

    [Fact]
    public async Task DeleteUser_TargetingSuperAdmin_AsAdmin_ReturnsNotFound()
    {
        var superAdmin = await CreateUserAsync(RoleNames.SuperAdmin);
        var controller = CreateController(callerRoles: RoleNames.Admin);

        var result = await controller.DeleteUser(superAdmin.Id);

        Assert.IsType<NotFoundResult>(result);
        Assert.NotNull(await _userManager.FindByIdAsync(superAdmin.Id.ToString()));
    }

    private static CreateMemberRequest BuildMemberRequest(Guid userId, string? prcLicenseNo = null) => new(
        UserId: userId,
        MembershipNo: Guid.NewGuid().ToString("N")[..8],
        FirstName: "Test",
        MiddleName: null,
        LastName: "User",
        Suffix: null,
        Birthdate: null,
        Gender: null,
        CivilStatus: null,
        EducationLevel: null,
        SchoolName: null,
        CourseYearGraduated: null,
        SpecifiedProfession: null,
        MobileNumber: null,
        HouseNo: null,
        Street: null,
        Barangay: null,
        CityMunicipality: null,
        Province: null,
        ZipCode: null,
        Country: null,
        MailingHouseNo: null,
        MailingStreet: null,
        MailingBarangay: null,
        MailingCityMunicipality: null,
        MailingProvince: null,
        MailingZipCode: null,
        MailingCountry: null,
        HousePhone: null,
        PrcLicenseNo: prcLicenseNo,
        PrcRegistrationDate: null,
        PrcValidUntilDate: null,
        PtrNumber: null, PtrPlaceIssued: null, PtrDateIssued: null,
        Tin: null,
        Chapter: Chapters.Ncr, ChapterYear: null, ChapterPosition: null,
        EmploymentStatus: null,
        Company: null,
        Position: null,
        BusinessAddress: null,
        YearsOfPractice: null,
        Specialization: null,
        Skills: null,
        MemberType: MemberTypes.Regular,
        RenewalDueDate: null,
        NationalDuesReferenceNo: null);

    [Fact]
    public async Task DeleteUser_WithPrcVerificationHistory_ReturnsConflict()
    {
        var user = await CreateUserAsync(RoleNames.Member);
        var created = await _memberService.CreateAsync(BuildMemberRequest(user.Id, prcLicenseNo: "MP-1"));
        var member = Assert.IsType<MemberDto>(created.Value);
        await _memberService.ApprovePrcVerificationAsync(member.Id, Guid.NewGuid());

        var result = await _controller.DeleteUser(user.Id);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(await _userManager.FindByIdAsync(user.Id.ToString()));
    }

    [Fact]
    public async Task DeleteUser_RemovesUploadsAndCertificates()
    {
        var user = await CreateUserAsync(RoleNames.Member);
        await using (var stream = new MemoryStream([1, 2, 3, 4]))
        {
            await _memberCertificateService.UploadAsync(user.Id, stream, "cert.pdf", stream.Length);
        }
        var certificatesBefore = await _memberCertificateService.ListAsync(user.Id);
        Assert.Single(certificatesBefore);

        var result = await _controller.DeleteUser(user.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await _memberCertificateService.ListAsync(user.Id));
    }

    [Fact]
    public async Task AssignRole_ThenRemoveRole_UpdatesUsersRoles()
    {
        var user = await CreateUserAsync(RoleNames.Member);

        var assignResult = await _controller.AssignRole(user.Id, new AdminController.AssignRoleRequest(RoleNames.Accounts));
        Assert.IsType<NoContentResult>(assignResult);
        Assert.Contains(RoleNames.Accounts, await _userManager.GetRolesAsync(user));

        var removeResult = await _controller.RemoveRole(user.Id, new AdminController.AssignRoleRequest(RoleNames.Accounts));
        Assert.IsType<NoContentResult>(removeResult);
        Assert.DoesNotContain(RoleNames.Accounts, await _userManager.GetRolesAsync(user));
    }

    [Fact]
    public async Task AssignRole_WithUnknownRole_ReturnsBadRequest()
    {
        var user = await CreateUserAsync(RoleNames.Member);

        var result = await _controller.AssignRole(user.Id, new AdminController.AssignRoleRequest("Not A Real Role"));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task RemoveRole_RequestingSuperAdminRole_ReturnsForbidden()
    {
        var superAdmin = await CreateUserAsync(RoleNames.SuperAdmin);

        var result = await _controller.RemoveRole(superAdmin.Id, new AdminController.AssignRoleRequest(RoleNames.SuperAdmin));

        Assert.IsType<ForbidResult>(result);
        Assert.Contains(RoleNames.SuperAdmin, await _userManager.GetRolesAsync(superAdmin));
    }

    [Fact]
    public async Task AssignRole_RequestingSuperAdminRole_ReturnsForbidden()
    {
        var user = await CreateUserAsync(RoleNames.Member);

        var result = await _controller.AssignRole(user.Id, new AdminController.AssignRoleRequest(RoleNames.SuperAdmin));

        Assert.IsType<ForbidResult>(result);
        Assert.DoesNotContain(RoleNames.SuperAdmin, await _userManager.GetRolesAsync(user));
    }

    [Fact]
    public async Task AssignRole_TargetingExistingSuperAdmin_ReturnsForbidden()
    {
        var superAdmin = await CreateUserAsync(RoleNames.SuperAdmin);

        var result = await _controller.AssignRole(superAdmin.Id, new AdminController.AssignRoleRequest(RoleNames.Manager));

        Assert.IsType<ForbidResult>(result);
        Assert.DoesNotContain(RoleNames.Manager, await _userManager.GetRolesAsync(superAdmin));
    }

    [Fact]
    public async Task VerifyEmail_UnconfirmedUser_ConfirmsEmail()
    {
        var user = await CreateUserAsync(RoleNames.Member);
        Assert.False(user.EmailConfirmed);

        var result = await _controller.VerifyEmail(user.Id);

        Assert.IsType<NoContentResult>(result);
        var updated = await _userManager.FindByIdAsync(user.Id.ToString());
        Assert.True(updated!.EmailConfirmed);
    }

    [Fact]
    public async Task VerifyEmail_AlreadyConfirmedUser_IsNoOp()
    {
        var user = await CreateUserAsync(RoleNames.Member);
        await _userManager.ConfirmEmailAsync(user, await _userManager.GenerateEmailConfirmationTokenAsync(user));

        var result = await _controller.VerifyEmail(user.Id);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task VerifyEmail_TargetingSuperAdmin_AsAdmin_ReturnsNotFound()
    {
        var superAdmin = await CreateUserAsync(RoleNames.SuperAdmin);
        var controller = CreateController(callerRoles: RoleNames.Admin);

        var result = await controller.VerifyEmail(superAdmin.Id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task VerifyEmail_TargetingSelfAsSuperAdmin_ReturnsForbidden()
    {
        var superAdmin = await CreateUserAsync(RoleNames.SuperAdmin);
        var controller = CreateController(callerId: superAdmin.Id, callerRoles: RoleNames.SuperAdmin);

        var result = await controller.VerifyEmail(superAdmin.Id);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task CreateUser_RequestingSuperAdminRole_ReturnsForbidden_EvenAsSuperAdminCaller()
    {
        var request = new AdminController.CreateUserRequest($"{Guid.NewGuid()}@example.com", "New Super Admin", "Password123!", RoleNames.SuperAdmin);

        var result = await _controller.CreateUser(request);

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Null(await _userManager.FindByEmailAsync(request.Email));
    }

    [Fact]
    public async Task GetRoles_NeverReturnsSuperAdmin()
    {
        var result = await _controller.GetRoles();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var roles = Assert.IsAssignableFrom<IReadOnlyList<AdminController.RoleSummaryDto>>(ok.Value);
        Assert.DoesNotContain(roles, r => r.Name == RoleNames.SuperAdmin);
    }

    [Fact]
    public async Task UpdateRolePermissions_TargetingSuperAdminRole_ReturnsForbidden()
    {
        var superAdminRole = await _roleManager.FindByNameAsync(RoleNames.SuperAdmin);
        Assert.NotNull(superAdminRole);

        var result = await _controller.UpdateRolePermissions(superAdminRole!.Id, new AdminController.UpdateRolePermissionsRequest([]));

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetRoles_ReturnsAllNonSuperAdminRolesWithSeededPermissions()
    {
        var result = await _controller.GetRoles();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var roles = Assert.IsAssignableFrom<IReadOnlyList<AdminController.RoleSummaryDto>>(ok.Value);
        Assert.Equal(RoleNames.All.Length - 1, roles.Count);
        Assert.DoesNotContain(roles, r => r.Name == RoleNames.SuperAdmin);

        var memberRole = Assert.Single(roles, r => r.Name == RoleNames.Member);
        Assert.Contains(Permissions.Content.Create, memberRole.Permissions);
        Assert.DoesNotContain(Permissions.Content.Delete, memberRole.Permissions);
    }

    [Fact]
    public async Task UpdateRolePermissions_AddsAndRemovesClaimsToMatchRequest()
    {
        var role = await _roleManager.FindByNameAsync(RoleNames.Accounts);
        Assert.NotNull(role);

        var newPermissions = new[] { Permissions.Content.Create, Permissions.Admin.ManageUsers };
        var updateResult = await _controller.UpdateRolePermissions(
            role!.Id, new AdminController.UpdateRolePermissionsRequest(newPermissions));

        Assert.IsType<NoContentResult>(updateResult);

        var claims = await _roleManager.GetClaimsAsync(role);
        var permissionValues = claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToList();
        Assert.Equal(newPermissions.OrderBy(p => p), permissionValues.OrderBy(p => p));
    }

    [Fact]
    public async Task UpdateRolePermissions_WithUnknownPermission_ReturnsBadRequest()
    {
        var role = await _roleManager.FindByNameAsync(RoleNames.Manager);
        Assert.NotNull(role);

        var result = await _controller.UpdateRolePermissions(
            role!.Id, new AdminController.UpdateRolePermissionsRequest(["not:a-real-permission"]));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void GetPermissions_ReturnsAllDefinedPermissions()
    {
        var result = _controller.GetPermissions();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var permissions = Assert.IsAssignableFrom<IReadOnlyList<string>>(ok.Value);
        Assert.Equal(Permissions.All.OrderBy(p => p), permissions.OrderBy(p => p));
    }

    [Fact]
    public async Task SendPasswordReset_ForAVerifiedAccount_Succeeds()
    {
        var user = await CreateUserAsync(RoleNames.Member);
        user.EmailConfirmed = true;
        await _userManager.UpdateAsync(user);

        var result = await _controller.SendPasswordReset(user.Id);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task SendPasswordReset_ForAnUnverifiedAccount_IsRefused()
    {
        // Mirrors ForgotPassword: mailing a reset to an unproven address undermines the reason
        // that rule exists. The admin has verify-email for this case.
        var user = await CreateUserAsync(RoleNames.Member);

        var result = await _controller.SendPasswordReset(user.Id);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(
            "EMAIL_NOT_CONFIRMED",
            badRequest.Value?.GetType().GetProperty("code")?.GetValue(badRequest.Value) as string);
    }

    [Fact]
    public async Task SendPasswordReset_TargetingASuperAdmin_IsRefused()
    {
        // Otherwise an Admin could aim a reset at the Super Admin account. Refused as NotFound
        // rather than Forbidden, because IsHiddenFromCallerAsync runs first and deliberately hides
        // Super Admin accounts from every other caller - a 403 would confirm the account exists.
        var superAdmin = await CreateUserAsync(RoleNames.SuperAdmin);
        superAdmin.EmailConfirmed = true;
        await _userManager.UpdateAsync(superAdmin);

        var result = await _controller.SendPasswordReset(superAdmin.Id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task SendPasswordReset_IgnoresThePerAddressEmailThrottle()
    {
        // Counting administrator sends against the member's own 3-per-hour allowance would let a
        // member's earlier attempts block the person trying to help them - the worse failure. This
        // pins a deliberate bypass that is otherwise invisible and easy to "fix" by mistake.
        var user = await CreateUserAsync(RoleNames.Member);
        user.EmailConfirmed = true;
        await _userManager.UpdateAsync(user);

        var throttle = _scope.ServiceProvider.GetRequiredService<IEmailSendThrottle>();
        for (var i = 0; i < 5; i++)
        {
            throttle.TryRecordSend(user.Email!);
        }
        Assert.False(throttle.TryRecordSend(user.Email!), "the address's own allowance should be spent");

        var result = await _controller.SendPasswordReset(user.Id);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task IdentitySeeder_SuperAdminRoleMissingAPermission_ReSeedingGrantsIt()
    {
        // Reproduces a stale environment: Super Admin's role was created before a permission
        // (e.g. Events.Manage) existed in Permissions.All, and - unlike every other role, which is
        // deliberately left alone once seeded so an admin's own edits via /admin/roles are never
        // clobbered - Super Admin isn't a customization surface at all (AdminController.GetRoles
        // excludes it from that UI entirely), so re-running the seeder must still reach it.
        var superAdminRole = await _roleManager.FindByNameAsync(RoleNames.SuperAdmin) ?? throw new InvalidOperationException("Super Admin role missing.");
        var claims = await _roleManager.GetClaimsAsync(superAdminRole);
        var eventsManageClaim = claims.Single(c => c.Type == Permissions.ClaimType && c.Value == Permissions.Events.Manage);
        await _roleManager.RemoveClaimAsync(superAdminRole, eventsManageClaim);
        Assert.DoesNotContain((await _roleManager.GetClaimsAsync(superAdminRole)), c => c.Value == Permissions.Events.Manage);

        await IdentitySeeder.SeedAsync(_roleManager, _userManager, _configuration, NullLogger.Instance);

        var restoredClaims = await _roleManager.GetClaimsAsync(superAdminRole);
        Assert.Contains(restoredClaims, c => c.Type == Permissions.ClaimType && c.Value == Permissions.Events.Manage);
    }

    [Fact]
    public async Task IdentitySeeder_OtherRoleMissingANonDefaultPermission_ReSeedingLeavesItAlone()
    {
        // The contrast case: Manager never had Events.Manage in its own defaults (only
        // Events.View) - re-seeding must not grant it one, since Manager (unlike Super Admin) is a
        // real /admin/roles customization surface and this would silently overrule an admin's own
        // choice not to grant it.
        var managerRole = await _roleManager.FindByNameAsync(RoleNames.Manager) ?? throw new InvalidOperationException("Manager role missing.");

        await IdentitySeeder.SeedAsync(_roleManager, _userManager, _configuration, NullLogger.Instance);

        var claims = await _roleManager.GetClaimsAsync(managerRole);
        Assert.DoesNotContain(claims, c => c.Type == Permissions.ClaimType && c.Value == Permissions.Events.Manage);
    }
}
