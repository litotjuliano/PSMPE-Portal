namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// One row per audited event, from any domain - a single generic table rather than a dedicated
/// history table per event type (see PrcVerificationHistory for that older pattern, and
/// add-audit-and-error-logs/proposal.md for why this one is generic). Rows are never updated,
/// only inserted and, for auth.* event types, eventually pruned - see LogRetentionService.
/// </summary>
public class AuditLog : BaseEntity
{
    public string EventType { get; set; } = string.Empty;
    public Guid? ActorUserId { get; set; }
    public string? ActorIp { get; set; }
    public string? TargetType { get; set; }
    public Guid? TargetId { get; set; }
    public string? Metadata { get; set; }
}
