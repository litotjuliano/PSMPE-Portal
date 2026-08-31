using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Payments;

/// <summary>
/// The effect of accepting an event-registration payment, in one place - the EventRegistration
/// counterpart to PaymentVerification.Apply (which is membership-specific: it dereferences
/// Member.ApprovedAt and computes RenewalDueDate, neither of which applies here). Two callers apply
/// it: PaymentService.VerifyAsync (a member's proof was accepted) and
/// PaymentService.RecordEventCashPaymentAsync (an admin recorded cash on the spot) - see
/// add-events-cpd-tracker/proposal.md.
/// </summary>
internal static class EventPaymentVerification
{
    public static void Apply(Payment payment, EventRegistration registration, Guid decidedByUserId)
    {
        registration.Status = EventRegistrationStatus.PaymentVerified;
        registration.UpdatedAt = DateTimeOffset.UtcNow;

        payment.Status = PaymentStatus.Verified;
        payment.RejectedReason = null;
        payment.DecidedByUserId = decidedByUserId;
        payment.DecidedAt = DateTimeOffset.UtcNow;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
