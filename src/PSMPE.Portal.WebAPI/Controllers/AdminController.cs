using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Authorization;
using PSMPE.Portal.Infrastructure.Authorization.Policies;

namespace PSMPE.Portal.WebAPI.Controllers;

/// <summary>
/// System-wide administrative actions. Listing users/roles requires Admin; creating/editing/
/// deleting users requires the admin:manage-users permission; changing role assignments and role
/// permissions requires Super Admin. Super Admin is never assignable/visible through this API,
/// for any caller including a Super Admin - it's provisioned only via seeding/config/direct DB.
/// A Super Admin's own account is visible to themselves (GetUsers/GetUserById) but fully
/// read-only (UpdateUser/DeleteUser/AssignRole/RemoveRole all reject any Super Admin target) -
/// see IsHiddenFromCallerAsync and IsSuperAdminAccountAsync.
/// </summary>
[ApiController]
[Route("api/admin")]
public class AdminController(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    ILogger<AdminController> logger) : ControllerBase
{
    public record UserSummaryDto(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles, DateTimeOffset CreatedAt);

    public record AssignRoleRequest(string Role);

    public record RoleSummaryDto(Guid Id, string Name, IReadOnlyList<string> Permissions);

    public record UpdateRolePermissionsRequest(IReadOnlyList<string> Permissions);

    public record CreateUserRequest(string Email, string DisplayName, string Password, string? Role);

    public record UpdateUserRequest(string DisplayName, string Email, string? NewPassword);

    [HttpGet("users")]
    [Authorize(Policy = PolicyNames.RequireAdmin)]
    public async Task<ActionResult<PagedResult<UserSummaryDto>>> GetUsers(
        int page = 1,
        int pageSize = 20,
        string sortBy = "displayName",
        string sortDir = "asc",
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<ApplicationUser> query = userManager.Users.AsNoTracking();

        var superAdminIds = (await userManager.GetUsersInRoleAsync(RoleNames.SuperAdmin))
            .Select(u => u.Id)
            .ToHashSet();
        if (superAdminIds.Count > 0)
        {
            // Every Super Admin is invisible here except the caller's own row, if they are one -
            // never another Super Admin's, even to a Super Admin caller.
            var callerId = CurrentUserId;
            query = query.Where(u => !superAdminIds.Contains(u.Id) || u.Id == callerId);
        }

        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = sortBy.ToLowerInvariant() switch
        {
            "email" => descending ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
            "createdat" => descending ? query.OrderByDescending(u => u.CreatedAt) : query.OrderBy(u => u.CreatedAt),
            _ => descending ? query.OrderByDescending(u => u.DisplayName) : query.OrderBy(u => u.DisplayName),
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var pageOfUsers = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var summaries = new List<UserSummaryDto>(pageOfUsers.Count);
        foreach (var user in pageOfUsers)
        {
            var roles = await userManager.GetRolesAsync(user);
            summaries.Add(new UserSummaryDto(user.Id, user.Email ?? string.Empty, user.DisplayName, roles.ToList(), user.CreatedAt));
        }

        return Ok(new PagedResult<UserSummaryDto>(summaries, totalCount, page, pageSize));
    }

    // TODO: add search and audit logging once the admin UI needs them.

    [HttpGet("users/{id:guid}")]
    [Authorize(Policy = PolicyNames.RequireAdmin)]
    public async Task<ActionResult<UserSummaryDto>> GetUserById(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null || await IsHiddenFromCallerAsync(user))
        {
            return NotFound();
        }

        var roles = await userManager.GetRolesAsync(user);
        return Ok(new UserSummaryDto(user.Id, user.Email ?? string.Empty, user.DisplayName, roles.ToList(), user.CreatedAt));
    }

    [HttpPost("users")]
    [RequirePermission(Permissions.Admin.ManageUsers)]
    public async Task<ActionResult<UserSummaryDto>> CreateUser(CreateUserRequest request)
    {
        var role = string.IsNullOrWhiteSpace(request.Role) ? RoleNames.Member : request.Role;
        if (!RoleNames.All.Contains(role))
        {
            return BadRequest(new { message = $"Unknown role '{role}'." });
        }

        // Super Admin is never assignable through the application, regardless of caller -
        // provisioned only via seeding/config/direct DB.
        if (role == RoleNames.SuperAdmin)
        {
            logger.LogWarning("Rejected attempt by {CallerId} to create a new user with the Super Admin role.", CurrentUserId);
            return Forbid();
        }

        // Only a Super Admin may assign anything above the default role - mirrors AssignRole/
        // RemoveRole, which are Super-Admin-only, so this endpoint can't be used to grant
        // privilege an Admin couldn't grant through the existing role-assignment endpoints.
        if (role != RoleNames.Member && !User.IsInRole(RoleNames.SuperAdmin))
        {
            return Forbid();
        }

        var existing = await userManager.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            return Conflict(new { message = "An account with this email already exists." });
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return ValidationProblem(new ValidationProblemDetails(
                result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description })));
        }

        await userManager.AddToRoleAsync(user, role);
        var roles = await userManager.GetRolesAsync(user);
        return Ok(new UserSummaryDto(user.Id, user.Email!, user.DisplayName, roles.ToList(), user.CreatedAt));
    }

    [HttpPut("users/{id:guid}")]
    [RequirePermission(Permissions.Admin.ManageUsers)]
    public async Task<IActionResult> UpdateUser(Guid id, UpdateUserRequest request)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null || await IsHiddenFromCallerAsync(user))
        {
            return NotFound();
        }

        if (await IsSuperAdminAccountAsync(user))
        {
            logger.LogWarning("Rejected attempt by {CallerId} to update Super Admin account {TargetId}.", CurrentUserId, user.Id);
            return Forbid();
        }

        if (!string.Equals(user.Email, request.Email, StringComparison.OrdinalIgnoreCase))
        {
            var existing = await userManager.FindByEmailAsync(request.Email);
            if (existing is not null && existing.Id != user.Id)
            {
                return Conflict(new { message = "An account with this email already exists." });
            }

            // UserName always mirrors Email in this app (see AuthController.Register, IdentitySeeder).
            await userManager.SetEmailAsync(user, request.Email);
            await userManager.SetUserNameAsync(user, request.Email);
        }

        user.DisplayName = request.DisplayName;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return ValidationProblem(new ValidationProblemDetails(
                updateResult.Errors.ToDictionary(e => e.Code, e => new[] { e.Description })));
        }

        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            // GeneratePasswordResetTokenAsync + ResetPasswordAsync is the Identity-idiomatic
            // admin-reset path - unlike RemovePasswordAsync/AddPasswordAsync, it doesn't require
            // the account to currently have no password.
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var resetResult = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
            if (!resetResult.Succeeded)
            {
                return ValidationProblem(new ValidationProblemDetails(
                    resetResult.Errors.ToDictionary(e => e.Code, e => new[] { e.Description })));
            }
        }

        return NoContent();
    }

    [HttpDelete("users/{id:guid}")]
    [RequirePermission(Permissions.Admin.ManageUsers)]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null || await IsHiddenFromCallerAsync(user))
        {
            return NotFound();
        }

        if (await IsSuperAdminAccountAsync(user))
        {
            logger.LogWarning("Rejected attempt by {CallerId} to delete Super Admin account {TargetId}.", CurrentUserId, user.Id);
            return Forbid();
        }

        if (string.Equals(CurrentUserId?.ToString(), id.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "You cannot delete your own account." });
        }

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return ValidationProblem(new ValidationProblemDetails(
                result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description })));
        }

        return NoContent();
    }

    [HttpPost("users/{id:guid}/roles")]
    [Authorize(Policy = PolicyNames.RequireSuperAdmin)]
    public async Task<IActionResult> AssignRole(Guid id, AssignRoleRequest request)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        if (!RoleNames.All.Contains(request.Role))
        {
            return BadRequest(new { message = $"Unknown role '{request.Role}'." });
        }

        if (request.Role == RoleNames.SuperAdmin || await IsSuperAdminAccountAsync(user))
        {
            logger.LogWarning(
                "Rejected attempt by {CallerId} to assign role '{Role}' involving Super Admin (target {TargetId}).",
                CurrentUserId, request.Role, user.Id);
            return Forbid();
        }

        var result = await userManager.AddToRoleAsync(user, request.Role);
        if (!result.Succeeded)
        {
            return ValidationProblem(new ValidationProblemDetails(
                result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description })));
        }

        return NoContent();
    }

    [HttpDelete("users/{id:guid}/roles")]
    [Authorize(Policy = PolicyNames.RequireSuperAdmin)]
    public async Task<IActionResult> RemoveRole(Guid id, AssignRoleRequest request)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        if (!RoleNames.All.Contains(request.Role))
        {
            return BadRequest(new { message = $"Unknown role '{request.Role}'." });
        }

        if (request.Role == RoleNames.SuperAdmin || await IsSuperAdminAccountAsync(user))
        {
            logger.LogWarning(
                "Rejected attempt by {CallerId} to remove role '{Role}' involving Super Admin (target {TargetId}).",
                CurrentUserId, request.Role, user.Id);
            return Forbid();
        }

        var result = await userManager.RemoveFromRoleAsync(user, request.Role);
        if (!result.Succeeded)
        {
            return ValidationProblem(new ValidationProblemDetails(
                result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description })));
        }

        return NoContent();
    }

    [HttpGet("roles")]
    [Authorize(Policy = PolicyNames.RequireAdmin)]
    public async Task<ActionResult<IReadOnlyList<RoleSummaryDto>>> GetRoles()
    {
        // Super Admin's role (and its full permission claim set) never leaves the server -
        // it isn't manageable through the app for any caller.
        var roles = await roleManager.Roles.AsNoTracking().Where(r => r.Name != RoleNames.SuperAdmin).ToListAsync();
        var summaries = new List<RoleSummaryDto>(roles.Count);
        foreach (var role in roles)
        {
            var claims = await roleManager.GetClaimsAsync(role);
            var permissions = claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToList();
            summaries.Add(new RoleSummaryDto(role.Id, role.Name ?? string.Empty, permissions));
        }

        return Ok(summaries);
    }

    [HttpPut("roles/{roleId:guid}/permissions")]
    [Authorize(Policy = PolicyNames.RequireSuperAdmin)]
    public async Task<IActionResult> UpdateRolePermissions(Guid roleId, UpdateRolePermissionsRequest request)
    {
        var role = await roleManager.FindByIdAsync(roleId.ToString());
        if (role is null)
        {
            return NotFound();
        }

        if (role.Name == RoleNames.SuperAdmin)
        {
            logger.LogWarning("Rejected attempt by {CallerId} to edit Super Admin's permissions.", CurrentUserId);
            return Forbid();
        }

        var unknown = request.Permissions.Where(p => !Permissions.All.Contains(p)).ToList();
        if (unknown.Count > 0)
        {
            return BadRequest(new { message = $"Unknown permission(s): {string.Join(", ", unknown)}." });
        }

        var currentClaims = await roleManager.GetClaimsAsync(role);
        var currentPermissions = currentClaims.Where(c => c.Type == Permissions.ClaimType).ToList();

        foreach (var claim in currentPermissions.Where(c => !request.Permissions.Contains(c.Value)))
        {
            await roleManager.RemoveClaimAsync(role, claim);
        }

        foreach (var permission in request.Permissions.Where(p => currentPermissions.All(c => c.Value != p)))
        {
            await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission));
        }

        return NoContent();
    }

    [HttpGet("permissions")]
    [Authorize(Policy = PolicyNames.RequireAdmin)]
    public ActionResult<IReadOnlyList<string>> GetPermissions() => Ok(Permissions.All);

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    /// <summary>Hidden (404) unless it's the caller's own Super Admin row.</summary>
    private async Task<bool> IsHiddenFromCallerAsync(ApplicationUser target)
    {
        if (!await userManager.IsInRoleAsync(target, RoleNames.SuperAdmin))
        {
            return false;
        }

        return target.Id != CurrentUserId;
    }

    /// <summary>
    /// Unconditional, regardless of caller - a Super Admin account is never mutable through this
    /// API, not even by itself. Used to reject Update/Delete/AssignRole/RemoveRole on any target
    /// that holds Super Admin.
    /// </summary>
    private Task<bool> IsSuperAdminAccountAsync(ApplicationUser target) =>
        userManager.IsInRoleAsync(target, RoleNames.SuperAdmin);
}
