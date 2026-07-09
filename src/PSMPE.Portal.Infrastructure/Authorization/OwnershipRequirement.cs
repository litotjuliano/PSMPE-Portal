using Microsoft.AspNetCore.Authorization;

namespace PSMPE.Portal.Infrastructure.Authorization;

/// <summary>Resource-based requirement: caller must own the resource, or be an Admin/Super Admin.</summary>
public class OwnershipRequirement : IAuthorizationRequirement;
