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

/// <summary>
/// PSMPE's configured fees. Read by the registration wizard (to show the total), the receipt, and
/// the admin fees screen.
/// </summary>
public record MembershipFeesDto(decimal MembershipFee, decimal ShippingFee, decimal AnnualDues)
{
    public decimal RegistrationTotal => MembershipFee + ShippingFee;
}

public record UpdateMembershipFeesRequest(decimal MembershipFee, decimal ShippingFee, decimal AnnualDues);
