namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// A PSMPE event or workshop (national convention, chapter seminar, technical workshop). Runs
/// face-to-face and via Zoom simultaneously, and each modality is accredited separately, so
/// CpdUnitsOnsite and CpdUnitsOnline are independently nullable ("TBD" until an admin sets them) -
/// see add-events-cpd-tracker/proposal.md. Chapter is null for a national/all-chapters event.
/// </summary>
public class Event : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Chapter { get; set; }
    public string? Venue { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public int? Capacity { get; set; }
    public decimal Fee { get; set; }
    public decimal? CpdUnitsOnsite { get; set; }
    public decimal? CpdUnitsOnline { get; set; }

    /// <summary>Always at least one row, even for an event with no separate lectures (a single
    /// session spanning StartsAt/EndsAt) - see EventService.CreateAsync. Attendance and CPD credit
    /// are tracked per session, never per event, so there is no special case for a single-session
    /// event anywhere else in the model.</summary>
    public ICollection<EventSession> Sessions { get; set; } = new List<EventSession>();
}
