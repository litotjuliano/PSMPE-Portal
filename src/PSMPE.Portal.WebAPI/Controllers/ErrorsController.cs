using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.WebAPI.Extensions;

namespace PSMPE.Portal.WebAPI.Controllers;

[ApiController]
[Route("api/errors")]
public class ErrorsController(IErrorLogService errorLogService, ICurrentUserService currentUserService) : ControllerBase
{
    public record FrontendErrorReportRequest(string Message, string? StackTrace, string? Url, string? ComponentStack);

    // ErrorLog.Metadata has no HasMaxLength (unbounded text column) - this endpoint is public and
    // unauthenticated, so ComponentStack is capped here rather than left to grow unbounded across
    // repeated calls within the rate limit's own window.
    private const int MaxComponentStackLength = 4000;

    /// <summary>No [Authorize] - a frontend error can happen before login (e.g. on the login
    /// page itself), and this endpoint still records the caller's identity when a valid
    /// session/token is present, via ICurrentUserService reading the same JWT middleware
    /// populates for every request regardless of whether the endpoint requires it.</summary>
    [HttpPost("frontend")]
    [EnableRateLimiting(RateLimitingServiceExtensions.ErrorReportPolicy)]
    public async Task<IActionResult> ReportFrontendError(FrontendErrorReportRequest request, CancellationToken cancellationToken)
    {
        var componentStack = request.ComponentStack is { Length: > MaxComponentStackLength }
            ? request.ComponentStack[..MaxComponentStackLength]
            : request.ComponentStack;

        await errorLogService.RecordAsync(
            ErrorSource.Frontend, exceptionType: null, request.Message, request.StackTrace,
            requestPath: null, requestMethod: null, request.Url, currentUserService.UserId,
            Request.Headers.UserAgent.ToString(),
            metadata: componentStack is not null
                ? JsonSerializer.Serialize(new { componentStack })
                : null,
            cancellationToken);

        return NoContent();
    }
}
