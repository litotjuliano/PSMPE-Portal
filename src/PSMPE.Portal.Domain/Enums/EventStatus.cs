namespace PSMPE.Portal.Domain.Enums;

/// <summary>
/// Whether an event is visible to anyone without Events.View/Events.Manage (i.e. everyone besides
/// admin/manager staff) - see EventService.GetAllAsync/GetByIdAsync's includeDrafts parameter. A
/// Draft event behaves as if it doesn't exist to a non-staff caller: absent from the list, 404 on
/// direct fetch, refused on registration. Just these two states - no Archived/Cancelled, since
/// nothing has asked for them yet.
/// </summary>
public enum EventStatus
{
    Draft,
    Published,
}
