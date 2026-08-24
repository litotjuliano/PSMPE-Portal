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
}
