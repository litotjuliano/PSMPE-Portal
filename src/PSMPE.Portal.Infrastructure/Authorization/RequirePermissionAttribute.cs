using Microsoft.AspNetCore.Authorization;

namespace PSMPE.Portal.Infrastructure.Authorization;

/// <summary>
/// Gates an action behind a single permission claim without needing a named policy
/// registered per permission in DependencyInjection.cs, e.g. [RequirePermission(Permissions.Content.Create)].
/// Still enforces authentication like a normal [Authorize] attribute.
/// </summary>
public class RequirePermissionAttribute(string permission) : AuthorizeAttribute, IAuthorizationRequirementData
{
    public string Permission { get; } = permission;

    public IEnumerable<IAuthorizationRequirement> GetRequirements()
    {
        yield return new PermissionRequirement(Permission);
    }
}
