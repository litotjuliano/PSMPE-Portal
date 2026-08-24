namespace PSMPE.Portal.Domain.Enums;

/// <summary>
/// Which of an event's two independently-accredited CPD unit values (Event.CpdUnitsOnsite /
/// Event.CpdUnitsOnline) applies to a given registration's credit. Chosen by the member at
/// registration time - see add-events-cpd-tracker/proposal.md's "CPD units are tracked per
/// modality" decision.
/// </summary>
public enum EventMode
{
    Onsite,
    Online,
}
