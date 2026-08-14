using Microsoft.Extensions.Logging;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Infrastructure.Services;

public class ErrorLogService(IApplicationDbContext db, ILogger<ErrorLogService> logger) : IErrorLogService
{
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
                ExceptionType = exceptionType,
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
}
