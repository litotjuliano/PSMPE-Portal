using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Payments.Dtos;

public record PaymentDto(
    Guid Id,
    Guid MemberId,
    /// <summary>Denormalised for the admin queue, which lists payments but is read by someone
    /// thinking about members.</summary>
    string MemberName,
    string? MembershipNo,
    PaymentKind Kind,
    decimal Amount,
    string? ReferenceNo,
    DateOnly PaidOn,
    bool HasProof,
    PaymentStatus Status,
    string? RejectedReason,
    DateTimeOffset? DecidedAt,
    DateOnly? CoversUntil,
    DateTimeOffset CreatedAt);

/// <summary>
/// Self-service: the member declares what they paid. No Kind - the server decides whether this is
/// a NewMembership or a Renewal from the member's own state, so a member can't claim a renewal for
/// an application that was never approved.
/// </summary>
public record SubmitPaymentRequest(
    decimal Amount,
    string? ReferenceNo,
    DateOnly PaidOn);

public record RejectPaymentRequest(string Reason);

/// <summary>POST /api/events/registrations/{id}/payment/cash's request body - just the amount, no
/// proof file, no reference number. See PaymentService.RecordEventCashPaymentAsync.</summary>
public record RecordCashPaymentRequest(decimal Amount);

/// <summary>
/// Where an admin-uploaded proof landed. The key is handed back to the caller only so the approval
/// request can reference it moments later - it is not exposed on PaymentDto, since it embeds the
/// member's surname, first name and birthdate.
/// </summary>
public record ProofUploadDto(string StorageKey);

/// <summary>
/// PSMPE's configured fees. Read by the registration wizard (to show the total), the receipt, and
/// the admin fees screen.
/// </summary>
public record MembershipFeesDto(decimal MembershipFee, decimal ShippingFee, decimal AnnualDues)
{
    public decimal RegistrationTotal => MembershipFee + ShippingFee;
}

public record UpdateMembershipFeesRequest(decimal MembershipFee, decimal ShippingFee, decimal AnnualDues);
