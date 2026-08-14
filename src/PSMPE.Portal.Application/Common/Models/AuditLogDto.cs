namespace PSMPE.Portal.Application.Common.Models;

public record AuditLogDto(
    Guid Id, string EventType, Guid? ActorUserId, string? ActorIp,
    string? TargetType, Guid? TargetId, string? Metadata, DateTimeOffset CreatedAt);
