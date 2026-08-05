using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Authorization;
using PSMPE.Portal.Infrastructure.Authorization.Policies;
using PSMPE.Portal.WebAPI.Extensions;

namespace PSMPE.Portal.WebAPI.Controllers;

/// <summary>
/// System-wide administrative actions. Listing users/roles requires Admin; creating a user
/// requires the admin:manage-users permission; editing/deleting a user, changing role
/// assignments, and role permissions all require Super Admin (an Admin's only remaining action on
/// another user's row is VerifyEmail). Super Admin is never assignable/visible through this API,
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
    ILogger<AdminController> logger,
    IMemberService memberService,
    IMemberUploadService memberUploadService,
    IMemberCertificateService memberCertificateService,
    IEmailSender emailSender,
    IConfiguration configuration) : ControllerBase
{
    /// <summary>
    /// DataPrivacyConsentAt/Version are null for accounts that never went through public
    /// registration (seeded, admin-created) - that reads as "no consent on record", not "refused".
    /// </summary>
    public record UserSummaryDto(
        Guid Id,
        string Email,
        string DisplayName,
        IReadOnlyList<string> Roles,
        DateTimeOffset CreatedAt,
        bool EmailConfirmed,
        DateTimeOffset? DataPrivacyConsentAt = null,
        string? DataPrivacyConsentVersion = null);

    public record AssignRoleRequest(string Role);

    public record RoleSummaryDto(Guid Id, string Name, IReadOnlyList<string> Permissions);

    public record UpdateRolePermissionsRequest(IReadOnlyList<string> Permissions);

    public record CreateUserRequest(string Email, string DisplayName, string Password, string? Role);

    public record UpdateUserRequest(string DisplayName, string Email, string? NewPassword);

    /// <summary>
    /// Confirms the nginx -> ForwardedHeaders trust chain end to end after a deploy. If this
    /// returns a 172.x address, X-Forwarded-For isn't reaching the app and every rate limit
    /// partition has collapsed into one bucket.
    /// </summary>
    [HttpGet("diagnostics/client-ip")]
    [Authorize(Policy = PolicyNames.RequireAdmin)]
    public ActionResult<object> GetClientIp() =>
        Ok(new { clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() });

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
            "emailconfirmed" => descending ? query.OrderByDescending(u => u.EmailConfirmed) : query.OrderBy(u => u.EmailConfirmed),
            _ => descending ? query.OrderByDescending(u => u.DisplayName) : query.OrderBy(u => u.DisplayName),
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var pageOfUsers = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var summaries = new List<UserSummaryDto>(pageOfUsers.Count);
        foreach (var user in pageOfUsers)
        {
            var roles = await userManager.GetRolesAsync(user);
            summaries.Add(new UserSummaryDto(user.Id, user.Email ?? string.Empty, user.DisplayName, roles.ToList(), user.CreatedAt, user.EmailConfirmed,
                user.DataPrivacyConsentAt, user.DataPrivacyConsentVersion));
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
        return Ok(new UserSummaryDto(user.Id, user.Email ?? string.Empty, user.DisplayName, roles.ToList(), user.CreatedAt, user.EmailConfirmed,
            user.DataPrivacyConsentAt, user.DataPrivacyConsentVersion));
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
        return Ok(new UserSummaryDto(user.Id, user.Email!, user.DisplayName, roles.ToList(), user.CreatedAt, user.EmailConfirmed));
    }

    [HttpPut("users/{id:guid}")]
    [Authorize(Policy = PolicyNames.RequireSuperAdmin)]
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
    [Authorize(Policy = PolicyNames.RequireSuperAdmin)]
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

        // Deleting the user cascades to their Member row (if any), which has a Restrict FK from
        // PrcVerificationHistory - checked here so it surfaces as a clean failure instead of a raw
        // DbUpdateException (same reasoning as MemberService.DeleteAsync's own Member-only path).
        if (await memberService.HasPrcVerificationHistoryAsync(user.Id))
        {
            return Conflict(new { message = "Cannot delete this user: they have RMP verification history on record." });
        }

        // MemberUploads/MemberCertificates have no FK relationship at all (by design - see
        // openspecs/members.md), so they'd otherwise be silently orphaned (rows and files both)
        // once the cascade removes the Member row below.
        await memberUploadService.DeleteAllForUserAsync(user.Id);
        await memberCertificateService.DeleteAllForUserAsync(user.Id);

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

    /// <summary>
    /// Emails the account the same reset link self-service forgot-password issues, so an
    /// administrator never learns or chooses the resulting password - unlike UpdateUser's
    /// NewPassword, which requires the admin to type one and pass it on out of band.
    ///
    /// Gated on the permission rather than a role policy so it can be granted or revoked through
    /// the roles UI without a deploy, and so it travels with admin:manage-users - "can create
    /// accounts" and "can help someone back into theirs" belong together.
    ///
    /// Doubles as the recovery path for a locked-out member: completing a reset clears LockoutEnd
    /// (see AuthController.ResetPassword), which otherwise needs direct database access.
    /// </summary>
    [HttpPost("users/{id:guid}/password-reset")]
    [RequirePermission(Permissions.Admin.ManageUsers)]
    public async Task<IActionResult> SendPasswordReset(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null || await IsHiddenFromCallerAsync(user))
        {
            return NotFound();
        }

        if (await IsSuperAdminAccountAsync(user))
        {
            logger.LogWarning("Rejected attempt by {CallerId} to send a password reset to Super Admin account {TargetId}.", CurrentUserId, user.Id);
            return Forbid();
        }

        if (!user.EmailConfirmed)
        {
            // Mirrors ForgotPassword: sending a reset to an unproven address undermines the reason
            // that rule exists. The admin has verify-email for this case.
            return BadRequest(new
            {
                message = "This account's email isn't verified yet. Verify it first, then send a password reset.",
                code = "EMAIL_NOT_CONFIRMED",
            });
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetLink = AuthLinks.ResetPassword(configuration, user.Id, token);

        // Deliberately not routed through IEmailSendThrottle. That cap is per address, and counting
        // administrator sends against it would let a member's own earlier attempts block the person
        // trying to help them - the worse failure. This endpoint is authenticated and
        // permission-gated, so it is not an open amplifier.
        await emailSender.SendEmailAsync(
            user.Email!,
            "Reset your PSMPE Portal password",
            $"<p>An administrator has started a password reset for your PSMPE Portal account. Click the link below to choose a new password:</p><p><a href=\"{resetLink}\">{resetLink}</a></p><p>If you weren't expecting this, you can safely ignore this email - your current password still works.</p>");

        // The closest thing to an audit trail this system has (see the audit logging item in the
        // backlog). An administrator acting on someone else's credentials should leave a trace.
        logger.LogInformation("Password reset email sent by {CallerId} for account {TargetId}.", CurrentUserId, user.Id);

        return NoContent();
    }

    [HttpPost("users/{id:guid}/verify-email")]
    [Authorize(Policy = PolicyNames.RequireAdmin)]
    public async Task<IActionResult> VerifyEmail(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null || await IsHiddenFromCallerAsync(user))
        {
            return NotFound();
        }

        if (await IsSuperAdminAccountAsync(user))
        {
            logger.LogWarning("Rejected attempt by {CallerId} to manually verify Super Admin account {TargetId}.", CurrentUserId, user.Id);
            return Forbid();
        }

        if (user.EmailConfirmed)
        {
            return NoContent();
        }

        user.EmailConfirmed = true;
        var result = await userManager.UpdateAsync(user);
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
