using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.Infrastructure.Services;

public class LogRetentionService(IApplicationDbContext db, IDateTimeProvider dateTimeProvider) : ILogRetentionService
{
    private const int AuditSecurityEventRetentionDays = 90;
    private const int ErrorLogRetentionDays = 30;

    public async Task PruneAsync(CancellationToken cancellationToken = default)
    {
        var now = dateTimeProvider.UtcNow;

        var auditCutoff = now.AddDays(-AuditSecurityEventRetentionDays);
        var staleAuditRows = await db.AuditLogs
            .Where(a => a.EventType.StartsWith("auth.") && a.CreatedAt < auditCutoff)
            .ToListAsync(cancellationToken);
        db.AuditLogs.RemoveRange(staleAuditRows);

        var errorCutoff = now.AddDays(-ErrorLogRetentionDays);
        var staleErrorRows = await db.ErrorLogs
            .Where(e => e.CreatedAt < errorCutoff)
            .ToListAsync(cancellationToken);
        db.ErrorLogs.RemoveRange(staleErrorRows);

        await db.SaveChangesAsync(cancellationToken);
    }
}
