namespace PSMPE.Portal.Domain.Enums;

/// <summary>
/// What a payment buys. NewMembership/Renewal both differ in what verifying them does (see
/// PaymentVerification.Apply). EventRegistration differs more sharply: verifying it does not touch
/// MembershipStatus or RenewalDueDate at all, it moves the linked EventRegistration.Status instead
/// (see EventPaymentVerification.Apply) - see add-events-cpd-tracker/proposal.md. PortalAccessOnly
/// is the mid-cycle add-on purchase (a member who's current on dues but never opted into portal
/// access) - verifying it must NOT advance RenewalDueDate the way Renewal does, only flip
/// HasPortalAccess.
/// </summary>
public enum PaymentKind
{
    NewMembership,
    Renewal,
    EventRegistration,
    PortalAccessOnly,
}
