namespace PSMPE.Portal.Application.Events.Dtos;

public record EventSessionDto(
    Guid Id,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int Order,
    /// <summary>Raw override, not resolved against the parent Event's Venue - a caller that needs
    /// the effective venue computes `Venue ?? event.Venue` itself (see EventRegisterModal.tsx).</summary>
    string? Venue);

public record EventDto(
    Guid Id,
    string Title,
    string? Description,
    string? Objectives,
    /// <summary>Free text against EventTypes.All - see Event.Type's doc comment.</summary>
    string? Type,
    string? Chapter,
    string? Venue,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    decimal? Hours,
    int? Capacity,
    int RegisteredCount,
    decimal FeeOnsite,
    decimal FeeOnline,
    /// <summary>Null means "TBD" - see Event.CpdUnitsOnsite's doc comment.</summary>
    decimal? CpdUnitsOnsite,
    decimal? CpdUnitsOnline,
    string? CpdCodeOnsite,
    string? CpdCodeOnline,
    /// <summary>Derived from PosterImageStorageKey being non-null, same pattern as
    /// PaymentDto.HasProof - the key itself is never exposed to the client.</summary>
    bool HasPoster,
    IReadOnlyList<EventSessionDto> Sessions);

public record CreateEventRequest(
    string Title,
    string? Description,
    string? Chapter,
    string? Venue,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int? Capacity,
    decimal FeeOnsite,
    decimal FeeOnline,
    string? Type = null,
    decimal? Hours = null,
    string? Objectives = null);

/// <summary>Id is null for a brand new session, set for an existing one being edited. Any existing
/// session whose Id is absent from the list is removed - see EventService.UpdateAsync. CpdUnitsOnsite/
/// CpdUnitsOnline are absent from CreateEventRequest - they start null/"TBD" and are only ever set
/// through this request, never at creation.</summary>
public record EventSessionRequest(
    Guid? Id, string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int Order, string? Venue = null);

public record UpdateEventRequest(
    string Title,
    string? Description,
    string? Chapter,
    string? Venue,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int? Capacity,
    decimal FeeOnsite,
    decimal FeeOnline,
    decimal? CpdUnitsOnsite,
    decimal? CpdUnitsOnline,
    IReadOnlyList<EventSessionRequest> Sessions,
    string? Type = null,
    decimal? Hours = null,
    string? Objectives = null,
    string? CpdCodeOnsite = null,
    string? CpdCodeOnline = null);
