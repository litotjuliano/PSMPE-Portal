using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// One row per unhandled exception, backend or frontend. Separate from AuditLog - see
/// add-audit-and-error-logs/proposal.md's "ErrorLog is a separate table" decision.
/// </summary>
public class ErrorLog : BaseEntity
{
    public ErrorSource Source { get; set; }
    public string? ExceptionType { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
    public string? RequestPath { get; set; }
    public string? RequestMethod { get; set; }
    public string? Url { get; set; }
    public Guid? UserId { get; set; }
    public string? UserAgent { get; set; }
    public string? Metadata { get; set; }
}
