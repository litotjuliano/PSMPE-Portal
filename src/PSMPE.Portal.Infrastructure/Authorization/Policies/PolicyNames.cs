namespace PSMPE.Portal.Infrastructure.Authorization.Policies;

public static class PolicyNames
{
    public const string RequireAdmin = "RequireAdmin";
    public const string RequireSuperAdmin = "RequireSuperAdmin";
    public const string ContentOwnerOrAdmin = "ContentOwnerOrAdmin";
}
