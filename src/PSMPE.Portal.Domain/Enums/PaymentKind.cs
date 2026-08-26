namespace PSMPE.Portal.Domain.Enums;

/// <summary>
/// What a payment buys. The two differ in what verifying them does: a NewMembership payment
/// activates the member and sets their first RenewalDueDate, while a Renewal advances the existing
/// one. See PaymentService.VerifyAsync.
/// </summary>
public enum PaymentKind
{
    NewMembership,
    Renewal,
}
