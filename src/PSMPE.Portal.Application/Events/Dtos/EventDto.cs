namespace PSMPE.Portal.Application.Events.Dtos;

public record EventSessionDto(Guid Id, string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int Order);

public record EventDto(
    Guid Id,
    string Title,
    string? Description,
    string? Chapter,
    string? Venue,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int? Capacity,
    int RegisteredCount,
    decimal Fee,
    /// <summary>Null means "TBD" - see Event.CpdUnitsOnsite's doc comment.</summary>
    decimal? CpdUnitsOnsite,
    decimal? CpdUnitsOnline,
    IReadOnlyList<EventSessionDto> Sessions);

public record CreateEventRequest(
    string Title,
    string? Description,
    string? Chapter,
    string? Venue,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int? Capacity,
    decimal Fee);

/// <summary>Id is null for a brand new session, set for an existing one being edited. Any existing
/// session whose Id is absent from the list is removed - see EventService.UpdateAsync. CpdUnitsOnsite/
/// CpdUnitsOnline are absent from CreateEventRequest - they start null/"TBD" and are only ever set
/// through this request, never at creation.</summary>
public record EventSessionRequest(Guid? Id, string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int Order);

public record UpdateEventRequest(
    string Title,
    string? Description,
    string? Chapter,
    string? Venue,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int? Capacity,
    decimal Fee,
    decimal? CpdUnitsOnsite,
    decimal? CpdUnitsOnline,
    IReadOnlyList<EventSessionRequest> Sessions);
