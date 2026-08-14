using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Services;

public class AuditLogService(IApplicationDbContext db, ILogger<AuditLogService> logger) : IAuditLogService
{
    public async Task RecordAsync(
        string eventType, Guid? actorUserId, string? actorIp, string? targetType, Guid? targetId,
        string? metadata, CancellationToken cancellationToken = default)
    {
        try
        {
            db.AuditLogs.Add(new AuditLog
            {
                EventType = eventType,
                ActorUserId = actorUserId,
                ActorIp = actorIp,
                TargetType = targetType,
                TargetId = targetId,
                Metadata = metadata,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort by design - see IAuditLogService.RecordAsync's doc comment. A failure
            // here must never turn a 429, a login, or an approval into a 500.
            logger.LogError(ex, "Failed to record audit log event {EventType}", eventType);
        }
    }
}
