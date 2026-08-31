namespace PSMPE.Portal.Domain.Enums;

/// <summary>
/// Walks forward: Registered -> PaymentSubmitted -> PaymentVerified -> Attended ->
/// EvaluationSubmitted, with Rejected/Cancelled as off-ramps. One EventRegistration row per member
/// per event carries this single status rather than separate registration/attendance/evaluation
/// tables - see add-events-cpd-tracker/proposal.md.
/// </summary>
public enum EventRegistrationStatus
{
    Registered,
    PaymentSubmitted,
    PaymentVerified,
    Attended,
    EvaluationSubmitted,
    Rejected,
    Cancelled,
}
