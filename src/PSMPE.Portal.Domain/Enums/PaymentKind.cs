namespace PSMPE.Portal.Domain.Enums;

/// <summary>
/// What a payment buys. NewMembership/Renewal both differ in what verifying them does (see
/// PaymentVerification.Apply). EventRegistration differs more sharply: verifying it does not touch
/// MembershipStatus or RenewalDueDate at all, it moves the linked EventRegistration.Status instead
/// (see EventPaymentVerification.Apply) - see add-events-cpd-tracker/proposal.md.
/// </summary>
public enum PaymentKind
{
    NewMembership,
    Renewal,
    EventRegistration,
}
