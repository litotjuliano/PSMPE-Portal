using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PSMPE.Portal.Application.Account;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.WebAPI.Controllers;

/// <summary>
/// What an account may change about *itself*, for every role. The missing third alongside
/// AdminController (other people's user records) and MembersController (membership data).
///
/// Deliberately role-agnostic: display name and password live on ApplicationUser, which every
/// account has, so restricting this would mean adding code to keep members out of something they
/// need. Before this existed no account of any role could change its own password or name, and
/// the Super Admin account could not be edited at all - every admin endpoint rejects a Super Admin
/// as a target, and there was no self-service path.
///
/// There is no GET here on purpose: the client already holds email/displayName/roles from the
/// sign-in response, and the update returns the new state. A read endpoint would be a second
/// source of truth to keep consistent, with no caller needing it.
/// </summary>
[ApiController]
[Authorize]
[Route("api/account")]
public class AccountController(UserManager<ApplicationUser> userManager) : ControllerBase
{
    /// <summary>Mirrors AuthController's set, so policy failures read identically in both flows
    /// rather than leaking Identity's raw error codes in one and not the other.</summary>
    private static readonly HashSet<string> PasswordPolicyErrorCodes =
    [
        "PasswordTooShort",
        "PasswordRequiresDigit",
        "PasswordRequiresUpper",
        "PasswordRequiresLower",
        "PasswordRequiresNonAlphanumeric",
    ];

    [HttpPut("me")]
    public async Task<ActionResult<AccountDto>> UpdateMyAccount(UpdateAccountRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(UpdateAccountRequest.DisplayName)] = ["Name is required."],
            }));
        }

        user.DisplayName = request.DisplayName.Trim();
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return ValidationProblem(new ValidationProblemDetails(
                result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description })));
        }

        // Returned so the caller can refresh its cached user. The JWT's ClaimTypes.Name still
        // carries the old value until the token expires - nothing server-side authorizes on it,
        // and the web client reads its own cache rather than the claim.
        var roles = await userManager.GetRolesAsync(user);
        return Ok(new AccountDto(user.Email!, user.DisplayName, roles.ToList()));
    }

    [HttpPost("me/password")]
    public async Task<IActionResult> ChangeMyPassword(ChangePasswordRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        // ChangePasswordAsync verifies the current password itself - never hand-roll that check.
        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => !PasswordPolicyErrorCodes.Contains(e.Code)))
            {
                // A non-policy failure means the current password was wrong. Stay generic: saying
                // which half failed tells a caller holding a stolen token whether it is worth
                // guessing further.
                return BadRequest(new { message = "Your current password is incorrect." });
            }

            return ValidationProblem(new ValidationProblemDetails(
                result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description })));
        }

        // Same treatment a completed reset gets: proving the current password is at least as strong
        // a claim as holding an emailed reset token, so it should release a lockout too.
        //
        // Note failures above deliberately do NOT call AccessFailedAsync. This endpoint already
        // requires a valid token, so it is not an anonymous guessing surface - and counting them
        // would let anyone holding a stale token lock the real owner out of their own account.
        await userManager.ResetAccessFailedCountAsync(user);
        await userManager.SetLockoutEndDateAsync(user, null);

        return NoContent();
    }
}
