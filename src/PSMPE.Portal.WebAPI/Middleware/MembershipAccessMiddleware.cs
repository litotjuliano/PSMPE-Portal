using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Authorization;

namespace PSMPE.Portal.WebAPI.Middleware;

/// <summary>
/// Restricts a member to an explicit allowlist of self-service endpoints - the ones they need to
/// actually renew - under any of three independent conditions: a fully Expired member (persisted
/// Status, kept in sync by MembershipLifecycleService's daily auto-flip), a member whose most
/// recently verified payment didn't include portal access (Member.HasPortalAccess, written
/// exclusively by PaymentVerification.Apply), or a "Member"-role account with no Member row at all
/// yet (registered but never submitted a membership application - self-registration only creates
/// the account/role, see AuthController.Register; the Member row is created later by
/// MemberService.SubmitMyProfileAsync). That last case used to fall through this middleware
/// entirely - "member is not null && ..." is false for a null member, so the account got full,
/// unrestricted portal access, including things like event registration, having never applied or
/// paid anything. A member failing more than one of these sees MEMBERSHIP_EXPIRED first, then
/// MEMBERSHIP_NOT_STARTED, then PORTAL_ACCESS_REQUIRED - see the ordering below. Staff/admin roles
/// (any role other than exactly "Member"), Active/grace-period members with portal access, and
/// Deactivated members (a distinct admin action, excluded from the portal-access check) are
/// unaffected. Sits after UseAuthorization() so normal authentication/permission failures are
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

        // A "Member"-role account with no Member row at all - registered (and past the Expired
        // check above, which is false for a null member) but never submitted an application. Checked
        // before the portal-access check below so this gets its own message rather than being
        // reported as a portal-access problem, which would be misleading (there's no renewal to add
        // it on - there's no membership yet at all).
        if (member is null)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                code = "MEMBERSHIP_NOT_STARTED",
                message = "Complete your membership application to access the portal.",
            });
            return;
        }

        // Independent of the expiry check above: portal access reflects only the member's most
        // recently verified payment (PaymentVerification.Apply), so a renewal that omits the add-on
        // revokes it even while Status stays Active. Deactivated is excluded, same as the expiry
        // checks in MemberService.ComputeIsExpired/ComputeIsInGracePeriod - it's a distinct admin
        // action, not a lapsed-payment state.
        if (!member.HasPortalAccess && member.Status != MembershipStatus.Deactivated)
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
