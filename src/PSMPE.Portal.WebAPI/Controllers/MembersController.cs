using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Application.Members.Dtos;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Authorization;

namespace PSMPE.Portal.WebAPI.Controllers;

/// <summary>
/// PSMPE professional membership profiles - distinct from AdminController's Users (login/role
/// accounts). Every Member has exactly one linked ApplicationUser, but not every ApplicationUser
/// has a Member profile (e.g. staff accounts). See openspecs/members.md.
/// </summary>
[ApiController]
[Authorize]
[Route("api/members")]
public class MembersController(IMemberService memberService, UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Members.View)]
    public async Task<ActionResult<PagedResult<MemberDto>>> GetAll(
        int page = 1, int pageSize = 20, string sortBy = "lastName", string sortDir = "asc", CancellationToken cancellationToken = default)
        => Ok(await memberService.GetAllAsync(page, pageSize, sortBy, sortDir, cancellationToken));

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.Members.View)]
    public async Task<ActionResult<MemberDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var member = await memberService.GetByIdAsync(id, cancellationToken);
        return member is null ? NotFound() : Ok(member);
    }

    [HttpGet("me")]
    public async Task<ActionResult<MemberDto>> GetMyProfile(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var member = await memberService.GetByUserIdAsync(userId.Value, cancellationToken);
        return member is null ? NotFound() : Ok(member);
    }

    [HttpPost]
    [RequirePermission(Permissions.Members.Manage)]
    public async Task<ActionResult<MemberDto>> Create(CreateMemberRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            return BadRequest(new { message = $"No user found with id '{request.UserId}'." });
        }

        if (await memberService.GetByUserIdAsync(request.UserId, cancellationToken) is not null)
        {
            return Conflict(new { message = "This user already has a Member profile." });
        }

        if (await memberService.MembershipNoExistsAsync(request.MembershipNo, cancellationToken))
        {
            return Conflict(new { message = $"Membership No. '{request.MembershipNo}' is already in use." });
        }

        var created = await memberService.CreateAsync(request, cancellationToken);
        return Ok(created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Members.Manage)]
    public async Task<IActionResult> Update(Guid id, UpdateMemberRequest request, CancellationToken cancellationToken)
    {
        var result = await memberService.UpdateAsync(id, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPut("me")]
    public async Task<ActionResult<MemberDto>> UpdateMyProfile(UpdateMyProfileRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var updated = await memberService.UpsertMyProfileAsync(userId.Value, request, cancellationToken);
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.Members.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await memberService.DeleteAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    private IActionResult ToActionResult(Result result)
    {
        if (result.Succeeded)
        {
            return NoContent();
        }

        return result.ErrorType switch
        {
            ResultErrorType.NotFound => NotFound(new { message = result.Error }),
            ResultErrorType.Forbidden => Forbid(),
            _ => BadRequest(new { message = result.Error })
        };
    }
}
