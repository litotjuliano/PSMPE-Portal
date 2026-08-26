using Microsoft.AspNetCore.Authorization;

namespace PSMPE.Portal.Infrastructure.Authorization;

/// <summary>Requirement carrying one or more permission claim values (e.g. "content:create") to
/// check for - satisfied when the caller holds any one of them.</summary>
public class PermissionRequirement(params string[] permissions) : IAuthorizationRequirement
{
    public IReadOnlyList<string> Permissions { get; } = permissions;
}
