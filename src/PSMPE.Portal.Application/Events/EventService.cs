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
        int page, int pageSize, string? search, string? chapter, bool upcomingOnly,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Events.AsNoTracking().Include(e => e.Sessions).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Same case-insensitive .ToLower().Contains() idiom used in MemberService.GetAllAsync -
            // Contains(string, StringComparison) doesn't translate to SQL against Npgsql.
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(e => e.Title.ToLower().Contains(normalizedSearch));
        }

        if (!string.IsNullOrWhiteSpace(chapter))
        {
            query = query.Where(e => e.Chapter == chapter);
        }

        if (upcomingOnly)
        {
            var now = DateTimeOffset.UtcNow;
            query = query.Where(e => e.EndsAt >= now);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var events = await query
            .OrderBy(e => e.StartsAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var eventIds = events.Select(e => e.Id).ToList();
        var registeredCounts = await db.EventRegistrations
            .Where(r => eventIds.Contains(r.EventId) && r.Status != EventRegistrationStatus.Cancelled)
            .GroupBy(r => r.EventId)
            .Select(g => new { EventId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.EventId, g => g.Count, cancellationToken);

        var items = events.Select(e => ToDto(e, registeredCounts.GetValueOrDefault(e.Id))).ToList();
        return new PagedResult<EventDto>(items, totalCount, page, pageSize);
    }

    public async Task<EventDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var @event = await db.Events.AsNoTracking().Include(e => e.Sessions).FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (@event is null)
        {
            return null;
        }

        var registeredCount = await db.EventRegistrations.CountAsync(
            r => r.EventId == id && r.Status != EventRegistrationStatus.Cancelled, cancellationToken);
        return ToDto(@event, registeredCount);
    }

    public async Task<Result<EventDto>> CreateAsync(CreateEventRequest request, CancellationToken cancellationToken = default)
    {
        var validation = ValidateCore(
            request.Title, request.StartsAt, request.EndsAt, request.Capacity,
            request.FeeOnsite, request.FeeOnline, request.Chapter, request.Type, request.Hours);
        if (validation is not null)
        {
            return Result<EventDto>.Failure(validation);
        }

        var @event = new Event
        {
            Title = request.Title.Trim(),
            Description = request.Description,
            Objectives = request.Objectives,
            Type = request.Type,
            Chapter = request.Chapter,
            Venue = request.Venue,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            Hours = request.Hours,
            Capacity = request.Capacity,
            FeeOnsite = request.FeeOnsite,
            FeeOnline = request.FeeOnline,
        };
        // Every event gets at least one session, even with no separate lectures - see
        // Event.Sessions's doc comment. Admins split this into real lectures via UpdateAsync.
        @event.Sessions.Add(new EventSession
        {
            Title = @event.Title,
            StartsAt = @event.StartsAt,
            EndsAt = @event.EndsAt,
            Order = 1,
        });

        db.Events.Add(@event);
        await db.SaveChangesAsync(cancellationToken);

        return Result<EventDto>.Success(ToDto(@event, registeredCount: 0));
    }

    public async Task<Result<EventDto>> UpdateAsync(Guid id, UpdateEventRequest request, CancellationToken cancellationToken = default)
    {
        var validation = ValidateCore(
            request.Title, request.StartsAt, request.EndsAt, request.Capacity,
            request.FeeOnsite, request.FeeOnline, request.Chapter, request.Type, request.Hours);
        if (validation is not null)
        {
            return Result<EventDto>.Failure(validation);
        }

        if (request.CpdUnitsOnsite is < 0 || request.CpdUnitsOnline is < 0)
        {
            return Result<EventDto>.Failure("CPD units can't be negative.");
        }

        if (request.Sessions.Count == 0)
        {
            return Result<EventDto>.Failure("An event needs at least one session.");
        }

        foreach (var session in request.Sessions)
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

        var requestedIds = request.Sessions.Where(s => s.Id.HasValue).Select(s => s.Id!.Value).ToHashSet();

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

        foreach (var sessionRequest in request.Sessions)
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
        @event.Chapter = request.Chapter;
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
        @event.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var registeredCount = await db.EventRegistrations.CountAsync(
            r => r.EventId == id && r.Status != EventRegistrationStatus.Cancelled, cancellationToken);
        return Result<EventDto>.Success(ToDto(@event, registeredCount));
    }

    private static string? ValidateCore(
        string title, DateTimeOffset startsAt, DateTimeOffset endsAt, int? capacity,
        decimal feeOnsite, decimal feeOnline, string? chapter, string? type, decimal? hours)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Title is required.";
        }
        if (endsAt <= startsAt)
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
        if (chapter is not null && !Chapters.All.Contains(chapter))
        {
            return $"'{chapter}' is not a recognized chapter.";
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

    private static EventDto ToDto(Event e, int registeredCount) =>
        new(e.Id, e.Title, e.Description, e.Objectives, e.Type, e.Chapter, e.Venue, e.StartsAt, e.EndsAt,
            e.Hours, e.Capacity, registeredCount, e.FeeOnsite, e.FeeOnline, e.CpdUnitsOnsite, e.CpdUnitsOnline,
            e.CpdCodeOnsite, e.CpdCodeOnline, e.PosterImageStorageKey is not null,
            e.Sessions.OrderBy(s => s.Order)
                .Select(s => new EventSessionDto(s.Id, s.Title, s.StartsAt, s.EndsAt, s.Order, s.Venue))
                .ToList());
}
