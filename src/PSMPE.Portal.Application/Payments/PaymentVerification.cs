using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Payments;

/// <summary>
/// The effect of accepting a payment, in one place.
///
/// Two callers apply it: <see cref="PaymentService.VerifyAsync"/> for a standalone decision (a
/// renewal, or a registration payment cleared after the fact), and MemberService.ApproveAsync,
/// which admits an application and accepts its registration payment in a single transaction.
/// Duplicating the due-date arithmetic between them would be the obvious way for the two paths to
/// drift apart, and it is the one calculation in this domain nobody can eyeball for correctness.
/// </summary>
internal static class PaymentVerification
{
    /// <summary>
    /// Caller must have already established that the payment is Submitted, has proof, and that the
    /// member is approved - <paramref name="member"/>.ApprovedAt is dereferenced for a
    /// NewMembership payment.
    /// </summary>
    public static void Apply(Payment payment, Member member, Guid decidedByUserId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        member.RenewalDueDate = payment.Kind switch
        {
            // First payment: one year from admission, matching the receipt's "Annual Dues are
            // payable one year after registration".
            PaymentKind.NewMembership => DateOnly.FromDateTime(member.ApprovedAt!.Value.UtcDateTime).AddYears(1),

            // Renewal: one year from the *previous* due date, so the anniversary is fixed.
            // Advancing from today would hand every late payer the grace period for free and
            // permanently shift their date each year.
            _ => (member.RenewalDueDate ?? today).AddYears(1),
        };

        member.Status = MembershipStatus.Active;

        // Recurring, not permanent: reflects only this payment. A renewal that omits the add-on
        // revokes access here, in the same call that would otherwise have granted it.
        member.HasPortalAccess = payment.IncludesPortalAccess;

        member.UpdatedAt = DateTimeOffset.UtcNow;

        payment.Status = PaymentStatus.Verified;
        payment.RejectedReason = null;
        payment.DecidedByUserId = decidedByUserId;
        payment.DecidedAt = DateTimeOffset.UtcNow;
        payment.CoversUntil = member.RenewalDueDate;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
