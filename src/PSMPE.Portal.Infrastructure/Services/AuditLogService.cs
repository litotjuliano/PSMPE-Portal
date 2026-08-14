using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
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

    public async Task<PagedResult<AuditLogDto>> GetPagedAsync(
        int page, int pageSize, string? search, string? eventType, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<AuditLog> query = db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim().ToLower();
            query = query.Where(a =>
                a.EventType.ToLower().Contains(normalized)
                || (a.TargetType != null && a.TargetType.ToLower().Contains(normalized))
                || (a.Metadata != null && a.Metadata.ToLower().Contains(normalized)));
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(a => a.EventType == eventType);
        }

        if (from is not null)
        {
            query = query.Where(a => a.CreatedAt >= from);
        }

        if (to is not null)
        {
            query = query.Where(a => a.CreatedAt <= to);
        }

        query = query.OrderByDescending(a => a.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new AuditLogDto(a.Id, a.EventType, a.ActorUserId, a.ActorIp, a.TargetType, a.TargetId, a.Metadata, a.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogDto>(items, totalCount, page, pageSize);
    }
}
