using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Common.Interfaces;

public interface IErrorLogService
{
    /// <summary>Best-effort: never throws, same contract as IAuditLogService.RecordAsync.
    /// Message/StackTrace are truncated to the configured maximum before being persisted, so an
    /// oversized value from an untrusted frontend report can't fail the write outright.</summary>
    Task RecordAsync(
        ErrorSource source, string? exceptionType, string message, string? stackTrace,
        string? requestPath, string? requestMethod, string? url, Guid? userId, string? userAgent,
        string? metadata, CancellationToken cancellationToken = default);

    Task<PagedResult<ErrorLogDto>> GetPagedAsync(
        int page, int pageSize, string? search, ErrorSource? source, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken cancellationToken = default);
}
