using Microsoft.AspNetCore.Authorization;

namespace PSMPE.Portal.Infrastructure.Authorization;

/// <summary>Resource-based requirement guarding system-wide actions (e.g. deleting a system Layout).</summary>
public class SystemAdminRequirement : IAuthorizationRequirement;
