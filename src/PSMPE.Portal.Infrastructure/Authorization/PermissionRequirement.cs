using Microsoft.AspNetCore.Authorization;

namespace PSMPE.Portal.Infrastructure.Authorization;

/// <summary>Requirement carrying a single permission claim value (e.g. "content:create") to check for.</summary>
public class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
