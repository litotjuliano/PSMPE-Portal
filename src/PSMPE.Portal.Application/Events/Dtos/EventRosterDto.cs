namespace PSMPE.Portal.Application.Events.Dtos;

public record EventRosterEntryDto(
    Guid RegistrationId,
    Guid MemberId,
    string MemberName,
    string? MembershipNo,
    string Mode,
    string Status,
    IReadOnlyList<Guid> AttendedSessionIds,
    int TotalSessions,
    Guid? PaymentId,
    string? PaymentStatus,
    /// <summary>True for a cash payment (no proof file ever attached), false for a proof-upload
    /// payment, null if there's no Payment on this registration yet. Derived from
    /// Payment.ProofStorageKey being null - a proof payment always has one attached before it can
    /// reach Verified (see PaymentService.VerifyAsync), so there's no need for a separate stored
    /// flag.</summary>
    bool? PaymentIsCash,
    string? PaymentRejectedReason,
    int? EvaluationRating,
    DateTimeOffset? EvaluationSubmittedAt,
    decimal? CreditUnits);

public record EventRosterDto(
    Guid EventId,
    string EventTitle,
    IReadOnlyList<EventSessionDto> Sessions,
    IReadOnlyList<EventRosterEntryDto> Registrants);
