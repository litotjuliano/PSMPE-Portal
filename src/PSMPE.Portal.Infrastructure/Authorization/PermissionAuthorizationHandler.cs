using Microsoft.AspNetCore.Authorization;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Infrastructure.Authorization;

/// <summary>
/// Succeeds when the caller's JWT carries a "permission" claim matching the requirement.
/// Permission claims are seeded onto roles (see IdentitySeeder) and embedded into the JWT
/// alongside role claims (see JwtTokenGenerator), so this is a pure claim check with no DB hit.
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasClaim(Permissions.ClaimType, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
