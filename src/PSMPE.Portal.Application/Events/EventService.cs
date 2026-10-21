using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Events.Dtos;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Events;

public partial class EventService(IApplicationDbContext db) : IEventService
{
    public async Task<PagedResult<EventDto>> GetAllAsync(
        int page, int pageSize, string? search, Guid? chapterId, bool upcomingOnly,
        Guid? currentUserId = null, bool includeDrafts = false, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Events.AsNoTracking().Include(e => e.Sessions).Include(e => e.Chapter).AsQueryable();

        if (!includeDrafts)
        {
            // A Draft event is invisible to anyone without Events.View/Events.Manage - see
            // EventStatus's doc comment. EventsController.GetAll passes includeDrafts=true only for
            // that staff cohort.
            query = query.Where(e => e.Status == EventStatus.Published);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Same case-insensitive .ToLower().Contains() idiom used in MemberService.GetAllAsync -
            // Contains(string, StringComparison) doesn't translate to SQL against Npgsql.
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(e => e.Title.ToLower().Contains(normalizedSearch));
        }

        if (chapterId is not null)
        {
            query = query.Where(e => e.ChapterId == chapterId);
        }

        if (upcomingOnly)
        {
            var now = DateTimeOffset.UtcNow;
            // An undated event is always a Draft (see Event.StartsAt) - keep it in the staff list
            // rather than letting the date comparison silently hide it.
            query = query.Where(e => e.EndsAt == null || e.EndsAt >= now);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var events = await query
            // Undated Drafts first so they're easy to find and finish; then soonest first.
            .OrderBy(e => e.StartsAt == null ? 0 : 1)
            .ThenBy(e => e.StartsAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var eventIds = events.Select(e => e.Id).ToList();
        var registeredCounts = await db.EventRegistrations
            .Where(r => eventIds.Contains(r.EventId) && r.Status != EventRegistrationStatus.Cancelled)
            .GroupBy(r => r.EventId)
            .Select(g => new { EventId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.EventId, g => g.Count, cancellationToken);

        var myRegistrations = await GetMyRegistrationsByEventAsync(currentUserId, eventIds, cancellationToken);

        var items = events
            .Select(e => ToDto(e, registeredCounts.GetValueOrDefault(e.Id), myRegistrations.GetValueOrDefault(e.Id)))
            .ToList();
        return new PagedResult<EventDto>(items, totalCount, page, pageSize);
    }

    public async Task<EventDto?> GetByIdAsync(
        Guid id, Guid? currentUserId = null, bool includeDrafts = false, CancellationToken cancellationToken = default)
    {
        var @event = await db.Events.AsNoTracking().Include(e => e.Sessions).Include(e => e.Chapter).FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (@event is null || (!includeDrafts && @event.Status != EventStatus.Published))
        {
            // A Draft event behaves as if it doesn't exist to a non-staff caller - same treatment
            // as a genuinely missing id, not a distinguishable "exists but hidden" response.
            return null;
        }

        var registeredCount = await db.EventRegistrations.CountAsync(
            r => r.EventId == id && r.Status != EventRegistrationStatus.Cancelled, cancellationToken);
        var myRegistrations = await GetMyRegistrationsByEventAsync(currentUserId, [id], cancellationToken);
        return ToDto(@event, registeredCount, myRegistrations.GetValueOrDefault(id));
    }

    /// <summary>The calling member's own non-cancelled registration per event, for the given event
    /// ids only. Empty (not an error) when currentUserId is null or belongs to no member - an admin
    /// browsing the Events list simply sees no "you're registered" badges. At most one row per event
    /// per member, same invariant RegisterAsync's duplicate-registration check relies on.</summary>
    private async Task<Dictionary<Guid, EventRegistration>> GetMyRegistrationsByEventAsync(
        Guid? currentUserId, IReadOnlyList<Guid> eventIds, CancellationToken cancellationToken)
    {
        if (currentUserId is null || eventIds.Count == 0)
        {
            return [];
        }

        var member = await db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == currentUserId, cancellationToken);
        if (member is null)
        {
            return [];
        }

        return await db.EventRegistrations
            .Where(r => r.MemberId == member.Id && eventIds.Contains(r.EventId) && r.Status != EventRegistrationStatus.Cancelled)
            .ToDictionaryAsync(r => r.EventId, cancellationToken);
    }

    public async Task<Result<EventDto>> CreateAsync(CreateEventRequest request, CancellationToken cancellationToken = default)
    {
        var validation = ValidateCore(
            request.Title, request.Description, request.StartsAt, request.EndsAt, request.Status, request.Capacity,
            request.FeeOnsite, request.FeeOnline, request.Type, request.Hours);
        if (validation is not null)
        {
            return Result<EventDto>.Failure(validation);
        }

        var (chapter, chapterError) = await ResolveChapterAsync(request.ChapterId, null, cancellationToken);
        if (chapterError is not null)
        {
            return Result<EventDto>.Failure(chapterError);
        }

        var @event = new Event
        {
            Title = request.Title.Trim(),
            Description = request.Description,
            Objectives = request.Objectives,
            Type = request.Type,
            ChapterId = request.ChapterId,
            Chapter = chapter,
            Venue = request.Venue,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            Hours = request.Hours,
            Capacity = request.Capacity,
            FeeOnsite = request.FeeOnsite,
            FeeOnline = request.FeeOnline,
            Status = request.Status,
        };
        // Every dated event gets at least one session, even with no separate lectures - see
        // Event.Sessions's doc comment. Admins split this into real lectures via UpdateAsync. An
        // undated Draft has nothing to span yet; UpdateAsync adds the session once dates are set.
        if (request.StartsAt is { } startsAt && request.EndsAt is { } endsAt)
        {
            @event.Sessions.Add(new EventSession
            {
                Title = @event.Title,
                StartsAt = startsAt,
                EndsAt = endsAt,
                Order = 1,
            });
        }

        db.Events.Add(@event);
        await db.SaveChangesAsync(cancellationToken);

        return Result<EventDto>.Success(ToDto(@event, registeredCount: 0));
    }

    public async Task<Result<EventDto>> UpdateAsync(Guid id, UpdateEventRequest request, CancellationToken cancellationToken = default)
    {
        var validation = ValidateCore(
            request.Title, request.Description, request.StartsAt, request.EndsAt, request.Status, request.Capacity,
            request.FeeOnsite, request.FeeOnline, request.Type, request.Hours);
        if (validation is not null)
        {
            return Result<EventDto>.Failure(validation);
        }

        if (request.CpdUnitsOnsite is < 0 || request.CpdUnitsOnline is < 0)
        {
            return Result<EventDto>.Failure("CPD units can't be negative.");
        }

        // A dated event always has at least one session. Dates set but no sessions sent (e.g. an
        // undated Draft that just got its dates) means "one session spanning the event", the same
        // default CreateAsync makes. With no dates (Draft only - ValidateCore) there are none yet.
        var requestedSessions = request.Sessions;
        if (requestedSessions.Count == 0 && request.StartsAt is { } eventStart && request.EndsAt is { } eventEnd)
        {
            requestedSessions = [new EventSessionRequest(null, request.Title.Trim(), eventStart, eventEnd, 1)];
        }

        foreach (var session in requestedSessions)
        {
            if (string.IsNullOrWhiteSpace(session.Title))
            {
                return Result<EventDto>.Failure("Every session needs a title.");
            }
            if (session.EndsAt <= session.StartsAt)
            {
                return Result<EventDto>.Failure("A session's end time must be after its start time.");
            }
        }

        var @event = await db.Events.Include(e => e.Sessions).FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (@event is null)
        {
            return Result<EventDto>.NotFound($"Event '{id}' was not found.");
        }

        var (chapter, chapterError) = await ResolveChapterAsync(request.ChapterId, @event.ChapterId, cancellationToken);
        if (chapterError is not null)
        {
            return Result<EventDto>.Failure(chapterError);
        }

        // Registrations, attendance, CPD credit and certificates all read the event's dates, so
        // once anyone has registered the dates can be changed but not cleared.
        if ((request.StartsAt is null || request.EndsAt is null)
            && await db.EventRegistrations.AnyAsync(r => r.EventId == id, cancellationToken))
        {
            return Result<EventDto>.Conflict("This event already has registrations, so its start and end dates can't be cleared.");
        }

        var requestedIds = requestedSessions.Where(s => s.Id.HasValue).Select(s => s.Id!.Value).ToHashSet();

        // Validate every referenced session Id actually belongs to this event before mutating
        // anything - a stale payload or an Id copy-pasted from another event's session must fail
        // cleanly here rather than throw partway through the mutation loop below.
        var existingSessionIds = @event.Sessions.Select(s => s.Id).ToHashSet();
        var unknownIds = requestedIds.Where(sid => !existingSessionIds.Contains(sid)).ToList();
        if (unknownIds.Count > 0)
        {
            return Result<EventDto>.Failure($"Session '{unknownIds[0]}' does not belong to this event.");
        }

        var removedSessions = @event.Sessions.Where(s => !requestedIds.Contains(s.Id)).ToList();
        if (removedSessions.Count > 0)
        {
            var removedIds = removedSessions.Select(s => s.Id).ToList();
            var hasAttendance = await db.EventAttendances.AnyAsync(a => removedIds.Contains(a.EventSessionId), cancellationToken);
            if (hasAttendance)
            {
                return Result<EventDto>.Conflict("One of the sessions being removed already has recorded attendance.");
            }
            foreach (var removed in removedSessions)
            {
                db.EventSessions.Remove(removed);
            }
        }

        foreach (var sessionRequest in requestedSessions)
        {
            if (sessionRequest.Id is { } sessionId)
            {
                // Safe to use First: the "unknown session Id" check above already guarantees
                // sessionId is one of @event.Sessions's Ids.
                var existing = @event.Sessions.First(s => s.Id == sessionId);
                existing.Title = sessionRequest.Title.Trim();
                existing.StartsAt = sessionRequest.StartsAt;
                existing.EndsAt = sessionRequest.EndsAt;
                existing.Order = sessionRequest.Order;
                existing.Venue = sessionRequest.Venue;
            }
            else
            {
                // Added via the DbSet (not @event.Sessions.Add) so EF marks it Added rather than
                // Modified: @event is already tracked (loaded, not newly Add()-ed), and EventSession's
                // client-generated non-default Guid key makes the navigation-fixup heuristic assume
                // an existing row otherwise, causing SaveChanges to attempt an UPDATE on a row that
                // doesn't exist yet.
                db.EventSessions.Add(new EventSession
                {
                    EventId = @event.Id,
                    Title = sessionRequest.Title.Trim(),
                    StartsAt = sessionRequest.StartsAt,
                    EndsAt = sessionRequest.EndsAt,
                    Order = sessionRequest.Order,
                    Venue = sessionRequest.Venue,
                });
            }
        }

        @event.Title = request.Title.Trim();
        @event.Description = request.Description;
        @event.Objectives = request.Objectives;
        @event.Type = request.Type;
        @event.ChapterId = request.ChapterId;
        @event.Chapter = chapter;
        @event.Venue = request.Venue;
        @event.StartsAt = request.StartsAt;
        @event.EndsAt = request.EndsAt;
        @event.Hours = request.Hours;
        @event.Capacity = request.Capacity;
        @event.FeeOnsite = request.FeeOnsite;
        @event.FeeOnline = request.FeeOnline;
        @event.CpdUnitsOnsite = request.CpdUnitsOnsite;
        @event.CpdUnitsOnline = request.CpdUnitsOnline;
        @event.CpdCodeOnsite = request.CpdCodeOnsite;
        @event.CpdCodeOnline = request.CpdCodeOnline;
        @event.Status = request.Status;
        @event.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var registeredCount = await db.EventRegistrations.CountAsync(
            r => r.EventId == id && r.Status != EventRegistrationStatus.Cancelled, cancellationToken);
        return Result<EventDto>.Success(ToDto(@event, registeredCount));
    }

    /// <summary>Null chapterId means a national event. A chapter can't be newly chosen once
    /// inactive, but an event already on it (<paramref name="currentChapterId"/>) can be re-saved.</summary>
    private async Task<(Chapter? Chapter, string? Error)> ResolveChapterAsync(
        Guid? chapterId, Guid? currentChapterId, CancellationToken cancellationToken)
    {
        if (chapterId is null)
        {
            return (null, null);
        }

        var chapter = await db.Chapters.FirstOrDefaultAsync(c => c.Id == chapterId, cancellationToken);
        if (chapter is null)
        {
            return (null, "The selected chapter was not found.");
        }
        if (!chapter.IsActive && chapterId != currentChapterId)
        {
            return (null, $"Chapter '{chapter.Name}' is no longer available.");
        }
        return (chapter, null);
    }

    private static string? ValidateCore(
        string title, string? description, DateTimeOffset? startsAt, DateTimeOffset? endsAt, EventStatus status,
        int? capacity, decimal feeOnsite, decimal feeOnline, string? type, decimal? hours)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Title is required.";
        }
        if (string.IsNullOrWhiteSpace(description))
        {
            return "Description is required.";
        }
        // Dates are optional only while the event is a Draft - everything downstream of
        // publishing (registration, attendance, CPD, certificates) needs them.
        if (status == EventStatus.Published && (startsAt is null || endsAt is null))
        {
            return "Starts At and Ends At are required to publish an event.";
        }
        if (startsAt is not null && endsAt is not null && endsAt <= startsAt)
        {
            return "End time must be after the start time.";
        }
        if (capacity is < 1)
        {
            return "Capacity must be at least 1 if set.";
        }
        if (feeOnsite < 0 || feeOnline < 0)
        {
            return "Fee can't be negative.";
        }
        if (type is not null && !EventTypes.All.Contains(type))
        {
            return $"'{type}' is not a recognized event type.";
        }
        if (hours is < 0)
        {
            return "Hours can't be negative if set.";
        }
        return null;
    }

    private static EventDto ToDto(Event e, int registeredCount, EventRegistration? myRegistration = null) =>
        new(e.Id, e.Title, e.Description, e.Objectives, e.Type, e.Chapter?.Name, e.ChapterId, e.Venue, e.StartsAt, e.EndsAt,
            e.Hours, e.Capacity, registeredCount, e.FeeOnsite, e.FeeOnline, e.CpdUnitsOnsite, e.CpdUnitsOnline,
            e.CpdCodeOnsite, e.CpdCodeOnline, e.PosterImageStorageKey is not null,
            e.Sessions.OrderBy(s => s.Order)
                .Select(s => new EventSessionDto(s.Id, s.Title, s.StartsAt, s.EndsAt, s.Order, s.Venue))
                .ToList(),
            myRegistration?.Id, myRegistration?.Mode.ToString(), myRegistration?.Status.ToString(), e.Status);
}
