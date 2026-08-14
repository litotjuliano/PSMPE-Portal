using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Infrastructure.Services;

public class ErrorLogService(IApplicationDbContext db, ILogger<ErrorLogService> logger) : IErrorLogService
{
    private const int MaxExceptionTypeLength = 256;
    private const int MaxMessageLength = 2000;
    private const int MaxStackTraceLength = 8000;
    private const int MaxRequestPathOrUrlLength = 512;
    private const int MaxUserAgentLength = 512;
    private const int MaxRequestMethodLength = 16;

    public async Task RecordAsync(
        ErrorSource source, string? exceptionType, string message, string? stackTrace,
        string? requestPath, string? requestMethod, string? url, Guid? userId, string? userAgent,
        string? metadata, CancellationToken cancellationToken = default)
    {
        try
        {
            db.ErrorLogs.Add(new ErrorLog
            {
                Source = source,
                ExceptionType = Truncate(exceptionType, MaxExceptionTypeLength),
                Message = Truncate(message, MaxMessageLength) ?? string.Empty,
                StackTrace = Truncate(stackTrace, MaxStackTraceLength),
                RequestPath = Truncate(requestPath, MaxRequestPathOrUrlLength),
                RequestMethod = Truncate(requestMethod, MaxRequestMethodLength),
                Url = Truncate(url, MaxRequestPathOrUrlLength),
                UserId = userId,
                UserAgent = Truncate(userAgent, MaxUserAgentLength),
                Metadata = metadata,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort by design - see IErrorLogService.RecordAsync's doc comment. A failure
            // here must never turn the original error into a second, unrelated 500.
            logger.LogError(ex, "Failed to record error log entry for {Source}", source);
        }
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null : value.Length <= maxLength ? value : value[..maxLength];

    public async Task<PagedResult<ErrorLogDto>> GetPagedAsync(
        int page, int pageSize, string? search, ErrorSource? source, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<ErrorLog> query = db.ErrorLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim().ToLower();
            query = query.Where(e =>
                e.Message.ToLower().Contains(normalized)
                || (e.ExceptionType != null && e.ExceptionType.ToLower().Contains(normalized))
                || (e.Url != null && e.Url.ToLower().Contains(normalized))
                || (e.RequestPath != null && e.RequestPath.ToLower().Contains(normalized)));
        }

        if (source is not null)
        {
            query = query.Where(e => e.Source == source);
        }

        if (from is not null)
        {
            query = query.Where(e => e.CreatedAt >= from);
        }

        if (to is not null)
        {
            query = query.Where(e => e.CreatedAt <= to);
        }

        query = query.OrderByDescending(e => e.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new ErrorLogDto(
                e.Id, e.Source, e.ExceptionType, e.Message, e.StackTrace, e.RequestPath, e.RequestMethod,
                e.Url, e.UserId, e.UserAgent, e.Metadata, e.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<ErrorLogDto>(items, totalCount, page, pageSize);
    }
}
