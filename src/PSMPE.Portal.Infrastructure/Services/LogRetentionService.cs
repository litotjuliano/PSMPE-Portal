using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.Infrastructure.Services;

public class LogRetentionService(IApplicationDbContext db, IDateTimeProvider dateTimeProvider) : ILogRetentionService
{
    private const int AuditSecurityEventRetentionDays = 90;

    public async Task PruneAsync(CancellationToken cancellationToken = default)
    {
        var auditCutoff = dateTimeProvider.UtcNow.AddDays(-AuditSecurityEventRetentionDays);
        var staleAuditRows = await db.AuditLogs
            .Where(a => a.EventType.StartsWith("auth.") && a.CreatedAt < auditCutoff)
            .ToListAsync(cancellationToken);
        db.AuditLogs.RemoveRange(staleAuditRows);

        await db.SaveChangesAsync(cancellationToken);
    }
}
