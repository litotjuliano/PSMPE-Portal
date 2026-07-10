using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
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

    public AdminControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _scope = factory.Services.CreateScope();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _roleManager = _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
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
        return new AdminController(_userManager, _roleManager)
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
    public async Task GetUsers_IncludesSuperAdmin_WhenCallerIsSuperAdmin()
    {
        var superAdmin = await CreateUserAsync(RoleNames.SuperAdmin);

        var result = await _controller.GetUsers(page: 1, pageSize: 1000, cancellationToken: CancellationToken.None);

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
    public async Task DeleteUser_LastRemainingSuperAdmin_ReturnsBadRequest()
    {
        // Force exactly one Super Admin to exist, regardless of what other tests in this
        // shared-DB test class may have left behind, so the "last remaining" guard is
        // deterministic no matter what order tests run in.
        foreach (var other in await _userManager.GetUsersInRoleAsync(RoleNames.SuperAdmin))
        {
            await _userManager.DeleteAsync(other);
        }

        var superAdmin = await CreateUserAsync(RoleNames.SuperAdmin);
        var controller = CreateController(callerRoles: RoleNames.SuperAdmin);

        var result = await controller.DeleteUser(superAdmin.Id);

        Assert.IsType<BadRequestObjectResult>(result);
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
    public async Task RemoveRole_LastRemainingSuperAdmin_ReturnsBadRequest()
    {
        // Force exactly one Super Admin to exist first - see the identical comment on
        // DeleteUser_LastRemainingSuperAdmin_ReturnsBadRequest for why this is needed in a
        // shared-DB test class.
        foreach (var other in await _userManager.GetUsersInRoleAsync(RoleNames.SuperAdmin))
        {
            await _userManager.DeleteAsync(other);
        }

        var superAdmin = await CreateUserAsync(RoleNames.SuperAdmin);

        var result = await _controller.RemoveRole(superAdmin.Id, new AdminController.AssignRoleRequest(RoleNames.SuperAdmin));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains(RoleNames.SuperAdmin, await _userManager.GetRolesAsync(superAdmin));
    }

    [Fact]
    public async Task GetRoles_ReturnsAllFiveRolesWithSeededPermissions()
    {
        var result = await _controller.GetRoles();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var roles = Assert.IsAssignableFrom<IReadOnlyList<AdminController.RoleSummaryDto>>(ok.Value);
        Assert.Equal(RoleNames.All.Length, roles.Count);

        var superAdminRole = Assert.Single(roles, r => r.Name == RoleNames.SuperAdmin);
        Assert.Equal(Permissions.All.OrderBy(p => p), superAdminRole.Permissions.OrderBy(p => p));

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
}
