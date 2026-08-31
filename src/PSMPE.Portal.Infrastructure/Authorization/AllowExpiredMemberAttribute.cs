namespace PSMPE.Portal.Infrastructure.Authorization;

/// <summary>
/// Marks an action reachable even when the caller's own membership Status is Expired - see
/// MembershipAccessMiddleware. Applied only to the self-service endpoints a member needs in order
/// to actually renew: their own profile, uploads, account settings, and payment submission. Not an
/// ASP.NET Core authorization policy (unlike RequirePermissionAttribute) - it's a plain marker read
/// directly off endpoint metadata by the middleware.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowExpiredMemberAttribute : Attribute;
