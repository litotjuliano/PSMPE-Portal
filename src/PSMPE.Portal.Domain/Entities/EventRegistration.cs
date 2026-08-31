using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// One row per member per event - registration, payment progress, attendance and evaluation all
/// live on this single row via Status, mirroring Payment's single-row-with-status-enum shape (see
/// add-events-cpd-tracker/proposal.md). Mode is chosen at registration and decides which of
/// Event.CpdUnitsOnsite/CpdUnitsOnline applies to this registration's credit. CPD credit itself is
/// deliberately NOT a field here - it's computed from Status + Mode + attendance + Event's unit
/// values at read time (see Application/Events/CpdCredit.cs), so a unit value set or corrected
/// after the fact is instantly correct everywhere with no backfill. Which sessions were attended
/// lives on EventAttendance, not here - there is no AttendedAt/AttendedBy flag on this row.
/// </summary>
public class EventRegistration : BaseEntity
{
    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;

    public Guid MemberId { get; set; }
    public Member Member { get; set; } = null!;

    public EventMode Mode { get; set; }
    public EventRegistrationStatus Status { get; set; } = EventRegistrationStatus.Registered;

    /// <summary>1-5. Fixed field set, not admin-configurable per event, to keep this pass
    /// scoped - see proposal.md's "Not Built".</summary>
    public int? EvaluationRating { get; set; }
    public string? EvaluationComments { get; set; }
    public DateTimeOffset? EvaluationSubmittedAt { get; set; }
}
