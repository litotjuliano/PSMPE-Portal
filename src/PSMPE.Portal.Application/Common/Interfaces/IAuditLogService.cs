using PSMPE.Portal.Application.Common.Models;

namespace PSMPE.Portal.Application.Common.Interfaces;

public interface IAuditLogService
{
    /// <summary>Best-effort: never throws. A logging failure must not break the caller's
    /// request - see Task 3's test for the contract this guarantees.</summary>
    Task RecordAsync(
        string eventType, Guid? actorUserId, string? actorIp, string? targetType, Guid? targetId,
        string? metadata, CancellationToken cancellationToken = default);
}
