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
    /// <summary>Whether this payment included the optional portal-access add-on - see
    /// Payment.IncludesPortalAccess. Surfaced here so the admin queue can flag a payment whose
    /// Amount doesn't match what this flag implies, without a separate lookup.</summary>
    bool IncludesPortalAccess,
    string? ReferenceNo,
    DateOnly PaidOn,
    bool HasProof,
    PaymentStatus Status,
    string? RejectedReason,
    DateTimeOffset? DecidedAt,
    DateOnly? CoversUntil,
    DateTimeOffset CreatedAt,
    /// <summary>Set only when Kind is EventRegistration - "Event registration" alone doesn't tell
    /// an admin working the queue which event, and it's a fair question with several events running
    /// at once. Null for NewMembership/Renewal, which have nothing else to name.</summary>
    string? EventTitle = null,
    /// <summary>Set only when Kind is EventRegistration - lets the member's own Events/register
    /// modal find "the payment for this registration" out of their full payment history (via
    /// GET /api/payments/me) without a dedicated lookup endpoint. Null for NewMembership/Renewal.</summary>
    Guid? EventRegistrationId = null);

/// <summary>
/// Self-service: the member declares what they paid. No Kind - the server decides whether this is
/// a NewMembership or a Renewal from the member's own state, so a member can't claim a renewal for
/// an application that was never approved.
/// </summary>
public record SubmitPaymentRequest(
    decimal Amount,
    string? ReferenceNo,
    DateOnly PaidOn,
    /// <summary>Whether this payment includes the optional portal-access add-on - always the
    /// caller's own declared intent, never server-forced. No global mode to switch: ticked
    /// produces the "combined" total, left unticked the "separate" one.</summary>
    bool IncludePortalAccess = false,
    /// <summary>A standalone mid-cycle purchase of the portal-access add-on alone - not a renewal.
    /// When true, PaymentService.SubmitAsync overrides the derived Kind to PortalAccessOnly and
    /// forces IncludesPortalAccess regardless of IncludePortalAccess above. See
    /// PaymentVerification.Apply for why this Kind exists: verifying it must not advance
    /// RenewalDueDate the way a real Renewal does.</summary>
    bool PortalAccessOnly = false);

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
/// the admin fees screen. Portal access is always an optional add-on, so every total comes in a
/// with/without pair rather than one figure that silently ignores it.
/// </summary>
public record MembershipFeesDto(decimal MembershipFee, decimal ShippingFee, decimal AnnualDues, decimal PortalFee)
{
    public decimal RegistrationTotalWithoutPortal => MembershipFee + ShippingFee;
    public decimal RegistrationTotalWithPortal => MembershipFee + ShippingFee + PortalFee;
    public decimal RenewalTotalWithoutPortal => AnnualDues;
    public decimal RenewalTotalWithPortal => AnnualDues + PortalFee;
}

public record UpdateMembershipFeesRequest(decimal MembershipFee, decimal ShippingFee, decimal AnnualDues, decimal PortalFee);

/// <summary>A configured FeePromotion, for the admin Promotions panel. See FeePromotion for the
/// resolution mechanics and the overlap rule enforced at creation.</summary>
public record FeePromotionDto(
    Guid Id, string FeeKey, decimal PromoAmount, DateOnly StartDate, DateOnly EndDate,
    Guid CreatedByUserId, DateTimeOffset CreatedAt);

/// <summary>FeeKey must be one of the MembershipFeeKeys constants; StartDate/EndDate must not
/// overlap an existing promotion for the same FeeKey - see PaymentService.CreatePromotionAsync.</summary>
public record CreateFeePromotionRequest(string FeeKey, decimal PromoAmount, DateOnly StartDate, DateOnly EndDate);

/// <summary>Summary figures for a date range, driving the admin Payments tab's reporting panel.
/// Only NewMembership/Renewal payments count - EventRegistration is a separate revenue stream.
/// See PaymentService.GetReportSummaryAsync for the exact filter (Verified status, PaidOn in
/// range, inclusive on both ends).</summary>
public record PaymentReportSummaryDto(
    int MembershipOnlyCount, decimal MembershipOnlyTotal,
    int CombinedCount, decimal CombinedTotal,
    decimal PortalRevenueTotal);
