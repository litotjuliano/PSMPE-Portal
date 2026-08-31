namespace PSMPE.Portal.Application.Events;

using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

/// <summary>
/// CPD credit is computed here, never stored on EventRegistration - see the design note at the top
/// of tasks.md and add-events-cpd-tracker/proposal.md. A registration only counts once it has
/// completed the full loop (evaluation submitted) AND the applicable modality's unit count has been
/// set, and the value is prorated by how many of the event's sessions were actually attended.
/// </summary>
internal static class CpdCredit
{
    public static decimal? For(EventRegistration registration, Event @event, int sessionsAttended, int totalSessions)
    {
        if (registration.Status != EventRegistrationStatus.EvaluationSubmitted || totalSessions <= 0)
        {
            return null;
        }

        var unitsForMode = registration.Mode == EventMode.Onsite ? @event.CpdUnitsOnsite : @event.CpdUnitsOnline;
        if (unitsForMode is null)
        {
            return null;
        }

        // Rounded to match CpdUnitsOnsite/CpdUnitsOnline's own HasPrecision(6, 2) in
        // EventConfiguration.cs - the raw division can otherwise produce up to 28 decimal digits
        // for non-evenly-divisible attendance fractions (e.g. 8 * 1 / 3).
        var rawCredit = unitsForMode.Value * sessionsAttended / totalSessions;
        return Math.Round(rawCredit, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The PRC accreditation reference (Event.CpdCodeOnsite/CpdCodeOnline) for whichever modality
    /// the registration was made under - same Mode-based selection as For() above, but unconditional
    /// (the code is informational metadata, not gated on evaluation status or session attendance the
    /// way the credit amount is).
    /// </summary>
    public static string? CodeFor(EventRegistration registration, Event @event) =>
        registration.Mode == EventMode.Onsite ? @event.CpdCodeOnsite : @event.CpdCodeOnline;
}
