using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Infrastructure.Authorization.Policies;

namespace PSMPE.Portal.WebAPI.Controllers;

/// <summary>System-wide administrative actions. Listing users requires Admin; changing roles requires Super Admin.</summary>
[ApiController]
[Route("api/admin")]
public class AdminController(UserManager<ApplicationUser> userManager) : ControllerBase
{
    public record UserSummaryDto(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles);

    public record AssignRoleRequest(string Role);

    [HttpGet("users")]
    [Authorize(Policy = PolicyNames.RequireAdmin)]
    public async Task<ActionResult<IReadOnlyList<UserSummaryDto>>> GetUsers(CancellationToken cancellationToken)
    {
        var users = await userManager.Users.AsNoTracking().ToListAsync(cancellationToken);
        var summaries = new List<UserSummaryDto>(users.Count);
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            summaries.Add(new UserSummaryDto(user.Id, user.Email ?? string.Empty, user.DisplayName, roles.ToList()));
        }

        return Ok(summaries);
    }

    // TODO: add pagination, search, and audit logging once the admin UI needs them.

    [HttpPost("users/{id:guid}/roles")]
    [Authorize(Policy = PolicyNames.RequireSuperAdmin)]
    public async Task<IActionResult> AssignRole(Guid id, AssignRoleRequest request)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        if (!Domain.Enums.RoleNames.All.Contains(request.Role))
        {
            return BadRequest(new { message = $"Unknown role '{request.Role}'." });
        }

        var result = await userManager.AddToRoleAsync(user, request.Role);
        if (!result.Succeeded)
        {
            return ValidationProblem(new ValidationProblemDetails(
                result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description })));
        }

        return NoContent();
    }
}
