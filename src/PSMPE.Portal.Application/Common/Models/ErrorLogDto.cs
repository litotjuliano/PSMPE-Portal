using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Common.Models;

public record ErrorLogDto(
    Guid Id, ErrorSource Source, string? ExceptionType, string Message, string? StackTrace,
    string? RequestPath, string? RequestMethod, string? Url, Guid? UserId, string? UserAgent,
    string? Metadata, DateTimeOffset CreatedAt);
