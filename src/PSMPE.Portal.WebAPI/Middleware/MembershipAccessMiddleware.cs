using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Authorization;

namespace PSMPE.Portal.WebAPI.Middleware;

/// <summary>
/// Restricts a member to an explicit allowlist of self-service endpoints - the ones they need to
/// actually renew - under either of two independent conditions: a fully Expired member (persisted
/// Status, kept in sync by MembershipLifecycleService's daily auto-flip), or a member whose most
/// recently verified payment didn't include portal access (Member.HasPortalAccess, written
/// exclusively by PaymentVerification.Apply). A member failing both sees MEMBERSHIP_EXPIRED - that
/// check runs first. Staff/admin roles (which never have a Member row), Active/grace-period members
/// with portal access, and Deactivated members (a distinct admin action, excluded from both checks)
/// are unaffected. Sits after UseAuthorization() so normal authentication/permission failures are
/// handled first, and before MapControllers() so a blocked request never reaches a controller
/// action.
/// </summary>
public class MembershipAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        if (context.GetEndpoint()?.Metadata.GetMetadata<AllowExpiredMemberAttribute>() is not null)
        {
            await next(context);
            return;
        }

        var currentUser = context.RequestServices.GetRequiredService<ICurrentUserService>();

        // Staff/admin accounts (any role other than exactly "Member") are never gated - the same
        // "administrative account" distinction MembersController/MyProfilePage.tsx already make.
        if (currentUser.Roles.Any(r => r != RoleNames.Member))
        {
            await next(context);
            return;
        }

        if (currentUser.UserId is not { } userId)
        {
            await next(context);
            return;
        }

        var memberService = context.RequestServices.GetRequiredService<IMemberService>();
        var member = await memberService.GetByUserIdAsync(userId, context.RequestAborted);

        // Status, not IsExpired - Status is authoritative post auto-flip and cheaper here (no
        // grace-period arithmetic needed for this check).
        if (member?.Status == MembershipStatus.Expired)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                code = "MEMBERSHIP_EXPIRED",
                message = "Your membership has expired. Renew your dues to restore full access.",
            });
            return;
        }

        // Independent of the expiry check above: portal access reflects only the member's most
        // recently verified payment (PaymentVerification.Apply), so a renewal that omits the add-on
        // revokes it even while Status stays Active. Deactivated is excluded, same as the expiry
        // checks in MemberService.ComputeIsExpired/ComputeIsInGracePeriod - it's a distinct admin
        // action, not a lapsed-payment state.
        if (member is not null && !member.HasPortalAccess && member.Status != MembershipStatus.Deactivated)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                code = "PORTAL_ACCESS_REQUIRED",
                message = "Your membership doesn't currently include portal access. Add it on your next renewal to restore full access.",
            });
            return;
        }

        await next(context);
    }
}
