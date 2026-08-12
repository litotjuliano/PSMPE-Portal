using Microsoft.AspNetCore.Authorization;

namespace PSMPE.Portal.Infrastructure.Authorization;

/// <summary>
/// Gates an action behind one or more permission claims without needing a named policy
/// registered per permission in DependencyInjection.cs, e.g. [RequirePermission(Permissions.Content.Create)].
/// Passing more than one permission is an OR - the caller needs only one of them, e.g.
/// [RequirePermission(Permissions.Members.Manage, Permissions.Members.Approve)].
/// Still enforces authentication like a normal [Authorize] attribute.
/// </summary>
public class RequirePermissionAttribute(params string[] permissions) : AuthorizeAttribute, IAuthorizationRequirementData
{
    public IReadOnlyList<string> Permissions { get; } = permissions;

    public IEnumerable<IAuthorizationRequirement> GetRequirements()
    {
        yield return new PermissionRequirement([.. Permissions]);
    }
}
