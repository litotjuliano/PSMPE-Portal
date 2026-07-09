using Microsoft.AspNetCore.Authorization;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Infrastructure.Authorization;

/// <summary>
/// Succeeds only for Super Admin when the resource is a system layout; for non-system layouts,
/// falls back to the normal owner-or-admin rule so regular users can still manage their own layouts.
/// </summary>
public class SystemAdminAuthorizationHandler : AuthorizationHandler<SystemAdminRequirement, Layout>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SystemAdminRequirement requirement,
        Layout resource)
    {
        if (resource.IsSystemLayout)
        {
            if (context.User.IsInRole(RoleNames.SuperAdmin))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }

        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var isOwner = Guid.TryParse(userId, out var parsedUserId) && resource.OwnerId == parsedUserId;
        var isAdmin = context.User.IsInRole(RoleNames.Admin) || context.User.IsInRole(RoleNames.SuperAdmin);

        if (isOwner || isAdmin)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
