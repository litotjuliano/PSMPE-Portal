using Microsoft.AspNetCore.Authorization;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Infrastructure.Authorization;

/// <summary>
/// Resource-based handler used via IAuthorizationService.AuthorizeAsync(User, contentItem, policy)
/// at the controller level. Succeeds when the caller owns the resource or holds an admin role.
/// Deliberately a single concrete handler rather than a generic engine — the CMS only has one
/// ownership concept today.
/// </summary>
public class OwnershipAuthorizationHandler : AuthorizationHandler<OwnershipRequirement, ContentItem>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OwnershipRequirement requirement,
        ContentItem resource)
    {
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
