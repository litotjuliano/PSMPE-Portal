using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// One row per admin decision on a member's PRC License No. - both first-time verification (a
/// never-reviewed submission) and later re-verification (a member-initiated change). CreatedAt
/// (from BaseEntity) doubles as "decided at" - no separate timestamp needed. DocumentStorageKey is
/// a best-effort snapshot of the PRC ID file in place at decision time - MemberUpload has no
/// versioning, so if the member reuploads again before this decision, the key may no longer point
/// at the exact file that was reviewed (see openspecs/members.md's known limitations).
/// </summary>
public class PrcVerificationHistory : BaseEntity
{
    public Guid MemberId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? DocumentStorageKey { get; set; }
    public PrcVerificationDecision Decision { get; set; }
    public string? Reason { get; set; }
    public Guid DecidedByUserId { get; set; }
}
