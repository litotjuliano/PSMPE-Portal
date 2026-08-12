namespace PSMPE.Portal.Domain.Enums;

/// <summary>
/// Deliberately distinct from MembershipStatus - this is the state of one payment, not of the
/// membership it pays for. A Rejected payment is kept rather than deleted so the member can see
/// why and resubmit.
/// </summary>
public enum PaymentStatus
{
    Submitted,
    Verified,
    Rejected,
}
