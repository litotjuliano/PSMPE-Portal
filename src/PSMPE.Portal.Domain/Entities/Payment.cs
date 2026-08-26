using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// One membership payment - either the initial fee at registration or an annual renewal - declared
/// by the member and decided on by an admin. Verifying one is the only thing that moves a member to
/// MembershipStatus.Active or advances their RenewalDueDate; before this entity existed both were
/// manual admin edits (see openspecs/payments.md).
///
/// The proof document is addressed by this row's own <see cref="ProofStorageKey"/> rather than a
/// MemberUpload row. MemberUpload is unique per (UserId, Kind) and deletes the previous file on
/// re-upload, so a renewal proof would have destroyed the registration proof - a payment record
/// whose evidence can vanish is not a record.
/// </summary>
public class Payment : BaseEntity
{
    public Guid MemberId { get; set; }
    public Member Member { get; set; } = null!;

    public PaymentKind Kind { get; set; }

    /// <summary>What the member says they paid, and when. Not validated against the configured fee -
    /// underpayments and overpayments both happen, and the admin sees the proof either way.</summary>
    public decimal Amount { get; set; }
    public string? ReferenceNo { get; set; }
    public DateOnly PaidOn { get; set; }

    /// <summary>Opaque IFileStorageService key for the deposit slip / transfer screenshot. Null only
    /// while a submission is being assembled - a payment can't be verified without one.</summary>
    public string? ProofStorageKey { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Submitted;

    /// <summary>Set on rejection and shown to the member so they know what to fix. Cleared if they
    /// resubmit and that submission is later approved.</summary>
    public string? RejectedReason { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>The RenewalDueDate this payment produced, recorded at verification. Without it the
    /// history says a payment was accepted but not what period it bought.</summary>
    public DateOnly? CoversUntil { get; set; }
}
