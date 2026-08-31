namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// One lecture/segment of a (possibly multi-day) Event - the unit attendance is actually tracked
/// against via EventAttendance. Order is a display sequence, not a uniqueness constraint - two
/// sessions sharing an Order value is a UI concern, not a data integrity one.
/// </summary>
public class EventSession : BaseEntity
{
    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public int Order { get; set; }

    /// <summary>Overrides Event.Venue for this session's display when set; falls back to
    /// Event.Venue when null. PRC's per-event schedule table shows a Venue column per date/session
    /// row, implying a multi-city or multi-room event's sessions can each have their own venue - see
    /// add-events-cpd-tracker/proposal.md's 2026-08-29 revision. The fallback itself is a
    /// display-time concern: EventDto/EventSessionDto carry the raw nullable override, not a
    /// resolved value, so an edit form can still tell "explicitly set to X" apart from "inherits the
    /// event's venue" (see EventFormModal.tsx / EventRegisterModal.tsx).</summary>
    public string? Venue { get; set; }
}
