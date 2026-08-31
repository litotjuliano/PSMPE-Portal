# Tasks: add-events-cpd-tracker (delta against the 2026-08-29 spec revision)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status: Implemented.** All 10 tasks complete via subagent-driven-development, each with a
spec-compliance and code-quality review pass (two fix cycles landed along the way — Task 4 gained
shared upload test helpers plus poster validation-failure coverage; Task 7 gained a poster-upload
failure split from event-save failure plus blob-URL revocation). Final whole-implementation review
confirmed 487/487 backend tests passing and a clean frontend build, with every renamed/new field
traced end to end from entity through to UI with no cross-task regressions. Two gaps surfaced by
that final review — `PaymentService` doesn't enforce submitted amounts against `Event.FeeOnsite`/
`FeeOnline` (spec.md's wording should soften to match this), and the certificate PDF doesn't yet
carry `CpdCodeOnsite`/`CpdCodeOnline`/`Type`/`Hours`/`Objectives` despite `proposal.md` saying it
should — are out of this plan's original scope and tracked separately rather than silently folded
in here.

**Goal:** This is a **delta plan**, not a from-scratch build. `add-events-cpd-tracker` already has a
complete, working, tested implementation (22 commits) built against the 2026-08-24 revision of the
proposal — `Event`, `EventSession`, `EventRegistration`, `EventAttendance`, the full registration →
payment → attendance → evaluation → CPD-credit → certificate flow, `EventsController`, and the React
pages, all shipped. The proposal was then revised again on 2026-08-29 against PRC's public
accreditation data, adding: `Event.Fee` split into `FeeOnsite`/`FeeOnline`; new `Event.CpdCodeOnsite`/
`CpdCodeOnline`; new `Event.Type` (free text against a constants list); new `Event.Hours`; new
`Event.Objectives`; a new admin-uploaded `Event.PosterImageStorageKey`; a new `EventSession.Venue`
override. This plan implements exactly those seven additions on top of the existing code — every step
below says **Modify** against a real file that already exists, never **Create** for anything already
built. Two more proposal points turned out to already be correct in the existing code and need no
changes at all (see "Already Satisfied — No Code Changes" immediately below); read that section before
starting so you don't duplicate work that's already done.

**Architecture:** No new entities, no new tables. `Event` gains seven columns; `EventSession` gains
one. A new `EventPosterService` (mirroring how `MemberUploadService` is kept separate from
`MemberService`) owns the poster/banner image upload, reusing the same
validate-downscale-reencode-via-SkiaSharp pipeline `MemberUploadService` already uses for Member
Photo — but simpler, since a poster has exactly one allowed kind (image) and lives directly on
`Event.PosterImageStorageKey`, the same "key stored directly on the owning row" shape
`Payment.ProofStorageKey` already uses, not `MemberUpload`'s separate join-table shape. `EventDto`,
`CreateEventRequest`, `UpdateEventRequest`, `EventSessionDto`, and `EventSessionRequest` all grow new
fields; `EventService.ValidateCore`/`CreateAsync`/`UpdateAsync`/`ToDto` are extended to match. One new
EF Core migration (`AlterColumn`/`AddColumn`/`RenameColumn` — **not** a fresh `CreateTable`, since the
four tables already exist from `20260824054348_AddEventsAndCpdTracker`) captures the schema delta.

**Tech Stack:** Same as the existing implementation — .NET 8 + EF Core 8 (Npgsql in prod, EF InMemory
in Application unit tests), React 19 + Vite + TypeScript + Tailwind, plain axios, xUnit for both unit
(`PSMPE.Portal.Application.UnitTests`) and integration (`PSMPE.Portal.WebAPI.IntegrationTests`, real
HTTP via `WebApplicationFactory<Program>`) tests. Image handling continues to use SkiaSharp (already
referenced by `PSMPE.Portal.Application.csproj`).

**Sequencing:** Task 1 lays the domain/config/migration groundwork (Fee split, new fields, session
Venue). Task 2 updates the Application-layer DTOs and `EventService`, plus the existing unit tests that
break from the `Fee` rename. Task 3 builds the new `EventPosterService` and wires its two endpoints
into `EventsController`. Task 4 fixes the two existing test files that construct events with the old
`Fee` shape and adds new coverage (Type validation, session Venue round-trip, poster upload). Task 5
scaffolds and hand-corrects the EF migration. Tasks 6–9 update the frontend. Task 10 updates
`openspecs/events.md` and runs final verification.

---

## Already Satisfied — No Code Changes

Confirmed by reading the actual current code before writing this plan — do not re-implement these:

- **`GET /api/events` already has search + filter query params.** `EventsController.GetAll` and
  `EventService.GetAllAsync` (`src/PSMPE.Portal.Application/Events/EventService.cs`) already accept
  and apply `search` (case-insensitive `Title.Contains`), `chapter` (exact match), and `upcomingOnly`
  (`EndsAt >= now`). The frontend `EventsTable.tsx` already renders a search box, a chapter `<select>`,
  and an "Upcoming only" checkbox, all wired through `EventsPage.tsx` with debounced search. Nothing to
  add here.
- **`GET /api/events/{id}/roster` search/filter is already handled client-side and is sufficient.**
  `EventService.GetRosterAsync` returns the full registrant list for one event (never more than a few
  hundred rows in practice), and `EventRosterTable.tsx` already filters that in-memory list by name/
  membership no. and by payment status via local `useState`/`useMemo` — no server round trip needed
  per keystroke. This plan does **not** add `search`/`status` query params to the roster endpoint
  itself; the existing client-side filtering already satisfies "the admin roster supports search and
  filter."
- **`Event.Capacity` is already informational-only — never enforced.** `EventService.RegisterAsync`
  (`src/PSMPE.Portal.Application/Events/EventService.Registration.cs`) contains no reference to
  `Capacity` anywhere in its logic; the only registration guard is the one-non-cancelled-registration-
  per-member-per-event check. `ValidateCore`'s only capacity rule is `capacity is < 1` → "must be at
  least 1 if set," which is input sanitization, not enforcement. Confirmed correct as-is.
- **There is no `Category` field and no eligibility-restriction logic anywhere in the codebase.**
  Grepped the full `Event`/`EventService`/`EventsController` surface — no `Category` property, no
  eligibility/restriction check of any kind. `RegisterAsync` allows any authenticated member with a
  `Member` profile to register for any event. Confirmed correct as-is.

---

## 1. Domain entities and EF configuration

**Files:**
- Create: `src/PSMPE.Portal.Domain/Enums/EventTypes.cs`
- Modify: `src/PSMPE.Portal.Domain/Entities/Event.cs`
- Modify: `src/PSMPE.Portal.Domain/Entities/EventSession.cs`
- Modify: `src/PSMPE.Portal.Infrastructure/Persistence/Configurations/EventConfiguration.cs`
- Modify: `src/PSMPE.Portal.Infrastructure/Persistence/Configurations/EventSessionConfiguration.cs`

Pure data classes and EF mapping — no behavior to TDD; verification is a successful build
(`dotnet build`) at the end of this task.

- [x] **Step 1: Create the `EventTypes` constants class**

```csharp
namespace PSMPE.Portal.Domain.Enums;

/// <summary>
/// Mirrors MemberTypes.cs exactly: free text against a constants list, not a validated C# enum -
/// see add-events-cpd-tracker/proposal.md's 2026-08-29 revision. Nothing validates Event.Type
/// itself; these constants only drive EventService.ValidateCore and the admin form's dropdown.
/// </summary>
public static class EventTypes
{
    public const string Conference = "Conference";
    public const string Seminar = "Seminar";
    public const string Technoforum = "Technoforum";
    public const string Convention = "Convention";
    public const string Symposium = "Symposium";
    public const string Expo = "Expo";

    public static readonly string[] All = [Conference, Seminar, Technoforum, Convention, Symposium, Expo];
}
```

- [x] **Step 2: Modify `Event.cs`** — replace the single `Fee` property with `FeeOnsite`/`FeeOnline`
      and add `Objectives`, `Type`, `Hours`, `CpdCodeOnsite`, `CpdCodeOnline`,
      `PosterImageStorageKey`. Full replacement content:

```csharp
namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// A PSMPE event or workshop (national convention, chapter seminar, technical workshop). Runs
/// face-to-face and via Zoom simultaneously, and each modality is accredited separately, so
/// CpdUnitsOnsite/CpdUnitsOnline (and FeeOnsite/FeeOnline, CpdCodeOnsite/CpdCodeOnline) are
/// independently nullable/settable - see add-events-cpd-tracker/proposal.md's 2026-08-29 revision
/// against PRC's public accreditation data. Chapter is null for a national/all-chapters event.
/// </summary>
public class Event : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Same shape/validation as Description - long text, informational only, shown on the
    /// event detail view and the certificate. Added per PRC's public program listings, which
    /// always carry a stated objective.</summary>
    public string? Objectives { get; set; }

    /// <summary>Free text against the EventTypes constants list (Conference, Seminar, Technoforum,
    /// Convention, Symposium, Expo) - mirrors Member.MemberType/MemberTypes exactly. Nothing
    /// validates the column itself; EventService.ValidateCore is what checks it against
    /// EventTypes.All.</summary>
    public string? Type { get; set; }

    public string? Chapter { get; set; }
    public string? Venue { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }

    /// <summary>PRC's declared hour count for the program - a single value shared across both
    /// modalities (PRC's own data shows the same hour count regardless of Onsite/Online).</summary>
    public decimal? Hours { get; set; }

    /// <summary>Informational planning target only - EventService.RegisterAsync never reads this
    /// field, so reaching it never blocks a new registration. See proposal.md's "Not Built".</summary>
    public int? Capacity { get; set; }

    /// <summary>Independent per-modality fee, replacing the original single Fee field - PRC's
    /// public accreditation data shows PSMPE's Onsite and Online programs are priced independently
    /// (e.g. PHP 3,000 Onsite vs PHP 900 Online for the same physical event).</summary>
    public decimal FeeOnsite { get; set; }
    public decimal FeeOnline { get; set; }

    public decimal? CpdUnitsOnsite { get; set; }
    public decimal? CpdUnitsOnline { get; set; }

    /// <summary>PRC's own accreditation reference for each modality's program - informational only,
    /// never validated against PRC. Independently nullable/settable exactly like CpdUnitsOnsite/
    /// CpdUnitsOnline, for the same reason: each modality is its own separate CPDAS submission.</summary>
    public string? CpdCodeOnsite { get; set; }
    public string? CpdCodeOnline { get; set; }

    /// <summary>Same shape as MemberUpload.StorageKey/Payment.ProofStorageKey - set by
    /// EventPosterService, never directly through CreateEventRequest/UpdateEventRequest. Null means
    /// no poster has been uploaded yet.</summary>
    public string? PosterImageStorageKey { get; set; }

    /// <summary>Always at least one row, even for an event with no separate lectures (a single
    /// session spanning StartsAt/EndsAt) - see EventService.CreateAsync. Attendance and CPD credit
    /// are tracked per session, never per event, so there is no special case for a single-session
    /// event anywhere else in the model.</summary>
    public ICollection<EventSession> Sessions { get; set; } = new List<EventSession>();
}
```

- [x] **Step 3: Modify `EventSession.cs`** — add the `Venue` override. Full replacement content:

```csharp
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
```

- [x] **Step 4: Modify `EventConfiguration.cs`** — replace `Fee`'s mapping with `FeeOnsite`/
      `FeeOnline`, and map the five new columns. Full replacement content:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.Property(e => e.Title).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Description).HasMaxLength(4000);
        builder.Property(e => e.Objectives).HasMaxLength(4000);
        builder.Property(e => e.Type).HasMaxLength(64);
        builder.Property(e => e.Chapter).HasMaxLength(64);
        builder.Property(e => e.Venue).HasMaxLength(256);
        builder.Property(e => e.Hours).HasPrecision(6, 2);
        builder.Property(e => e.FeeOnsite).HasPrecision(12, 2);
        builder.Property(e => e.FeeOnline).HasPrecision(12, 2);
        builder.Property(e => e.CpdUnitsOnsite).HasPrecision(6, 2);
        builder.Property(e => e.CpdUnitsOnline).HasPrecision(6, 2);
        builder.Property(e => e.CpdCodeOnsite).HasMaxLength(64);
        builder.Property(e => e.CpdCodeOnline).HasMaxLength(64);
        builder.Property(e => e.PosterImageStorageKey).HasMaxLength(512);

        // The events list filters/sorts on StartsAt; the admin roster looks events up by id only.
        builder.HasIndex(e => e.StartsAt);
    }
}
```

- [x] **Step 5: Modify `EventSessionConfiguration.cs`** — add the `Venue` mapping (one new line,
      everything else unchanged):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class EventSessionConfiguration : IEntityTypeConfiguration<EventSession>
{
    public void Configure(EntityTypeBuilder<EventSession> builder)
    {
        builder.Property(s => s.Title).IsRequired().HasMaxLength(256);
        builder.Property(s => s.Venue).HasMaxLength(256);

        builder.HasIndex(s => s.EventId);

        // Cascade, unlike every other FK in this feature - a session has no meaning outside its
        // event, so EventService.UpdateAsync's session reconciliation (add/edit/remove lectures)
        // is the only thing that ever removes one, and removing an Event's row entirely (not
        // supported by any endpoint in this pass) should take its sessions with it rather than
        // leaving them orphaned.
        builder.HasOne(s => s.Event)
            .WithMany(e => e.Sessions)
            .HasForeignKey(s => s.EventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [x] **Step 6: Verify the solution still builds** (it will not yet — `EventService.cs` and its
      tests still reference the old `Fee`/`EventSessionDto` shapes; Task 2 fixes that). Just confirm
      the compiler errors are limited to those expected spots:

Run: `dotnet build`
Expected: Errors only in `EventService.cs`, `EventDto.cs`, `EventServiceTests.cs`,
`PaymentServiceTests.cs`, and `EventsControllerTests.cs` (all fixed in later tasks below) — no errors
in `Event.cs`, `EventSession.cs`, `EventConfiguration.cs`, or `EventSessionConfiguration.cs`
themselves.

- [x] **Step 7: Commit**

```bash
git add src/PSMPE.Portal.Domain/Enums/EventTypes.cs src/PSMPE.Portal.Domain/Entities/Event.cs src/PSMPE.Portal.Domain/Entities/EventSession.cs src/PSMPE.Portal.Infrastructure/Persistence/Configurations/EventConfiguration.cs src/PSMPE.Portal.Infrastructure/Persistence/Configurations/EventSessionConfiguration.cs
git commit -m "feat: split Event.Fee into FeeOnsite/FeeOnline, add Type/Hours/Objectives/CpdCode/poster fields, add EventSession.Venue"
```

---

## 2. Application layer: DTOs and EventService

**Files:**
- Modify: `src/PSMPE.Portal.Application/Events/Dtos/EventDto.cs`
- Modify: `src/PSMPE.Portal.Application/Events/EventService.cs`
- Modify: `tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs`

- [x] **Step 1: Modify `EventDto.cs`** — full replacement content (new fields on `EventDto`,
      `EventSessionDto`, `CreateEventRequest`, `UpdateEventRequest`, `EventSessionRequest`; the new
      fields on the two request records default to `null` so every existing named-argument test call
      that doesn't mention them keeps compiling):

```csharp
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
```

- [x] **Step 2: Modify `EventService.cs`** — extend `ValidateCore`, `CreateAsync`, `UpdateAsync`, and
      `ToDto` to carry the new fields and validate `Type`/`Hours`/the split fee. Full replacement
      content:

```csharp
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
```

- [x] **Step 3: Fix the two broken helper methods in `EventServiceTests.cs`** — `ValidCreateRequest`
      (line ~15-17) currently ends `Capacity: 100, Fee: 500m)`; change the named argument. `ToUpdateRequest`
      (line ~222-225) currently reads `e.Capacity, e.Fee,`; change to the two split fields:

```csharp
    private static CreateEventRequest ValidCreateRequest(string title = "Water Sanitation Workshop") =>
        new(title, "Cross-connection control", Chapters.Ncr, "PICC", DateTimeOffset.UtcNow.AddDays(10),
            DateTimeOffset.UtcNow.AddDays(10).AddHours(4), Capacity: 100, FeeOnsite: 500m, FeeOnline: 200m);
```

```csharp
    private static UpdateEventRequest ToUpdateRequest(EventDto e) =>
        new(e.Title, e.Description, e.Chapter, e.Venue, e.StartsAt, e.EndsAt, e.Capacity, e.FeeOnsite, e.FeeOnline,
            e.CpdUnitsOnsite, e.CpdUnitsOnline,
            e.Sessions.Select(s => new EventSessionRequest(s.Id, s.Title, s.StartsAt, s.EndsAt, s.Order, s.Venue)).ToList());
```

- [x] **Step 4: Run the existing Event tests to confirm they still pass after the rename**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter FullyQualifiedName~EventServiceTests`
Expected: All previously-passing tests still PASS (no behavior changed yet, only the Fee shape).

- [x] **Step 5: Write three new failing tests for the genuinely new validation/behavior**, appended
      to `EventServiceTests.cs` (anywhere among the other `[Fact]` methods in the class):

```csharp
    [Fact]
    public async Task CreateAsync_UnrecognizedType_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);

        var result = await service.CreateAsync(ValidCreateRequest() with { Type = "Not A Real Type" });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task CreateAsync_RecognizedType_Succeeds()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);

        var result = await service.CreateAsync(ValidCreateRequest() with { Type = EventTypes.Seminar });

        Assert.True(result.Succeeded);
        Assert.Equal(EventTypes.Seminar, result.Value!.Type);
    }

    [Fact]
    public async Task UpdateAsync_SessionVenueOverride_PersistsAndFallsBackWhenCleared()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest() with { Venue = "PICC" })).Value!;
        var defaultSession = created.Sessions.Single();
        var withOverride = ToUpdateRequest(created) with
        {
            Sessions = [new EventSessionRequest(defaultSession.Id, defaultSession.Title, defaultSession.StartsAt, defaultSession.EndsAt, 1, "Cebu IT Park")],
        };

        var overridden = (await service.UpdateAsync(created.Id, withOverride)).Value!;
        Assert.Equal("Cebu IT Park", overridden.Sessions.Single().Venue);

        var cleared = ToUpdateRequest(overridden) with
        {
            Sessions = [new EventSessionRequest(defaultSession.Id, defaultSession.Title, defaultSession.StartsAt, defaultSession.EndsAt, 1, null)],
        };
        var result = await service.UpdateAsync(created.Id, cleared);

        Assert.Null(result.Value!.Sessions.Single().Venue);
    }
```

Add the necessary `using PSMPE.Portal.Domain.Enums;` at the top of the file if not already present
(it already is — `EventServiceTests.cs` already references `Chapters`/`EventMode` from that
namespace).

- [x] **Step 6: Run the new tests to verify they fail first (for the two genuinely new-behavior
      ones), then pass after Step 2's `EventService.cs` change**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter FullyQualifiedName~EventServiceTests`
Expected: PASS — `CreateAsync_UnrecognizedType_Fails`, `CreateAsync_RecognizedType_Succeeds`, and
`UpdateAsync_SessionVenueOverride_PersistsAndFallsBackWhenCleared` all green, alongside every
pre-existing test in the file.

- [x] **Step 7: Commit**

```bash
git add src/PSMPE.Portal.Application/Events/Dtos/EventDto.cs src/PSMPE.Portal.Application/Events/EventService.cs tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs
git commit -m "feat: extend EventDto/EventService for FeeOnsite/FeeOnline, Type, Hours, Objectives, CpdCode fields, and session Venue"
```

---

## 3. `EventPosterService` and its two endpoints

**Files:**
- Create: `src/PSMPE.Portal.Application/Events/IEventPosterService.cs`
- Create: `src/PSMPE.Portal.Application/Events/EventPosterService.cs`
- Modify: `src/PSMPE.Portal.Application/DependencyInjection.cs`
- Modify: `src/PSMPE.Portal.WebAPI/Controllers/EventsController.cs`

This mirrors `MemberUploadService`'s validate → downscale → re-encode → save pipeline
(`src/PSMPE.Portal.Application/Members/MemberUploadService.cs`), simplified: a poster is always an
image (no PDF case), there's exactly one per event, and the key lives directly on `Event`
(`PosterImageStorageKey`), not in a separate join table like `MemberUpload`. No dedicated unit test
file — exactly like `MemberUploadService`, which has no unit tests either (SkiaSharp decode/encode
needs a real image byte stream, which is exercised at the integration level instead, same as
`MemberUploadsTests.cs` does for member uploads). Task 4 adds the integration test for this.

- [x] **Step 1: Create `IEventPosterService.cs`**

```csharp
using PSMPE.Portal.Application.Common.Models;

namespace PSMPE.Portal.Application.Events;

public interface IEventPosterService
{
    Task<Result> UploadAsync(
        Guid eventId, Stream content, string fileName, long contentLength, CancellationToken cancellationToken = default);

    Task<(Stream Content, string ContentType)?> GetAsync(Guid eventId, CancellationToken cancellationToken = default);
}
```

- [x] **Step 2: Create `EventPosterService.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using SkiaSharp;

namespace PSMPE.Portal.Application.Events;

/// <summary>
/// Validates, downscales, and stores an Event's poster/banner image via IFileStorageService,
/// writing the resulting key directly onto Event.PosterImageStorageKey - same
/// validate-downscale-reencode-via-SkiaSharp pipeline MemberUploadService uses for Member Photo
/// (src/PSMPE.Portal.Application/Members/MemberUploadService.cs), simplified since a poster has
/// exactly one allowed kind (image, no PDF) and lives directly on the owning row rather than a
/// separate MemberUpload-style join table. See add-events-cpd-tracker/proposal.md's 2026-08-29
/// revision.
/// </summary>
public class EventPosterService(IApplicationDbContext db, IFileStorageService storage) : IEventPosterService
{
    private const long MaxPosterSizeBytes = 8 * 1024 * 1024;
    private const int MaxPosterDimension = 1600;
    private const int JpegQuality = 82;
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png"];

    public async Task<Result> UploadAsync(
        Guid eventId, Stream content, string fileName, long contentLength, CancellationToken cancellationToken = default)
    {
        var @event = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
        if (@event is null)
        {
            return Result.NotFound($"Event '{eventId}' was not found.");
        }

        if (contentLength == 0)
        {
            return Result.Failure("No file was provided.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return Result.Failure("Only JPG or PNG files are allowed.");
        }

        if (contentLength > MaxPosterSizeBytes)
        {
            return Result.Failure("File exceeds the 8 MB size limit.");
        }

        using var original = SKBitmap.Decode(content);
        if (original is null)
        {
            return Result.Failure("Could not read the image file - it may be corrupted.");
        }

        using var optimized = OptimizeImage(original);
        using var optimizedImage = SKImage.FromBitmap(optimized);
        using var jpegData = optimizedImage.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);

        var storageKey = $"events/{eventId}/poster.jpg";
        using var jpegStream = jpegData.AsStream();
        await storage.SaveAsync(storageKey, jpegStream, cancellationToken);

        @event.PosterImageStorageKey = storageKey;
        @event.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<(Stream Content, string ContentType)?> GetAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var storageKey = await db.Events.AsNoTracking()
            .Where(e => e.Id == eventId)
            .Select(e => e.PosterImageStorageKey)
            .FirstOrDefaultAsync(cancellationToken);
        if (storageKey is null)
        {
            return null;
        }

        var stream = await storage.OpenReadAsync(storageKey, cancellationToken);
        return stream is null ? null : (stream, "image/jpeg");
    }

    /// <summary>Downscales only (never upscales) so the longest side is at most MaxPosterDimension -
    /// same reasoning as MemberUploadService.OptimizeImage.</summary>
    private static SKBitmap OptimizeImage(SKBitmap original)
    {
        var longestSide = Math.Max(original.Width, original.Height);
        if (longestSide <= MaxPosterDimension)
        {
            return original.Copy();
        }

        var scale = (double)MaxPosterDimension / longestSide;
        var newWidth = (int)Math.Round(original.Width * scale);
        var newHeight = (int)Math.Round(original.Height * scale);

        var resized = original.Resize(new SKImageInfo(newWidth, newHeight), SKFilterQuality.High);
        return resized ?? original.Copy();
    }
}
```

- [x] **Step 3: Register the new service in `DependencyInjection.cs`** — add one line:

```csharp
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Content;
using PSMPE.Portal.Application.Events;
using PSMPE.Portal.Application.Layouts;
using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Application.Payments;

namespace PSMPE.Portal.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IContentService, ContentService>();
        services.AddScoped<ILayoutService, LayoutService>();
        services.AddScoped<IMemberService, MemberService>();
        services.AddScoped<IMemberUploadService, MemberUploadService>();
        services.AddScoped<IMemberCertificateService, MemberCertificateService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IEventPosterService, EventPosterService>();
        return services;
    }
}
```

- [x] **Step 4: Modify `EventsController.cs`** — inject `IEventPosterService` and add the two poster
      endpoints. Change the class declaration line and add two new action methods (placed after
      `Update`, before `Register`):

```csharp
public class EventsController(IEventService eventService, IPaymentService paymentService, IEventPosterService eventPosterService) : ControllerBase
{
```

```csharp
    /// <summary>Admin-only. Downscales/re-encodes via EventPosterService and overwrites any
    /// previous poster - an event has exactly one.</summary>
    [HttpPost("{id:guid}/poster")]
    [RequirePermission(Permissions.Events.Manage)]
    public async Task<IActionResult> UploadPoster(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var result = await eventPosterService.UploadAsync(id, stream, file.FileName, file.Length, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Any authenticated caller - same auth level as GetById, since the poster is shown on
    /// the member-facing events list/register views, not just to staff.</summary>
    [HttpGet("{id:guid}/poster")]
    public async Task<IActionResult> GetPoster(Guid id, CancellationToken cancellationToken)
    {
        var file = await eventPosterService.GetAsync(id, cancellationToken);
        return file is null ? NotFound() : File(file.Value.Content, file.Value.ContentType);
    }
```

Add `using PSMPE.Portal.Application.Events;` if not already imported (it already is, for
`IEventService`/`EventDto`).

- [x] **Step 5: Verify the solution builds**

Run: `dotnet build`
Expected: Build succeeds with zero errors.

- [x] **Step 6: Commit**

```bash
git add src/PSMPE.Portal.Application/Events/IEventPosterService.cs src/PSMPE.Portal.Application/Events/EventPosterService.cs src/PSMPE.Portal.Application/DependencyInjection.cs src/PSMPE.Portal.WebAPI/Controllers/EventsController.cs
git commit -m "feat: add EventPosterService and POST/GET /api/events/{id}/poster endpoints"
```

---

## 4. Fix remaining broken tests and add poster/fee integration coverage

**Files:**
- Modify: `tests/PSMPE.Portal.Application.UnitTests/Payments/PaymentServiceTests.cs`
- Modify: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Events/EventsControllerTests.cs`

- [x] **Step 1: Fix `SeedEventRegistrationAsync` in `PaymentServiceTests.cs`** (around line 291) —
      the entity literal still sets the old `Fee` property:

```csharp
        var @event = new Event { Title = "Seminar", StartsAt = DateTimeOffset.UtcNow.AddDays(5), EndsAt = DateTimeOffset.UtcNow.AddDays(5).AddHours(4), FeeOnsite = 500m, FeeOnline = 500m };
```

- [x] **Step 2: Run the Payments unit tests to confirm the fix compiles and passes**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter FullyQualifiedName~PaymentServiceTests`
Expected: PASS — all tests in the file, unchanged behavior.

- [x] **Step 3: Fix the three `fee = 500m,` JSON payload literals in `EventsControllerTests.cs`** —
      `ValidEventPayload` (~line 102) and the two inline update payloads (~line 164, ~line 227). In
      each of the three spots, replace the single line:

```csharp
        fee = 500m,
```

with:

```csharp
        feeOnsite = 500m,
        feeOnline = 200m,
```

(Match the existing indentation at each of the three call sites — `ValidEventPayload`'s body is
indented one level less than the two inline `Content = JsonContent.Create(new { ... })` payloads.)

- [x] **Step 4: Run the full Events integration test suite to confirm nothing else broke**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter FullyQualifiedName~EventsControllerTests`
Expected: PASS — every pre-existing test in the file still green.

- [x] **Step 5: Write a new failing integration test for the poster upload/download round trip**,
      appended to `EventsControllerTests.cs`:

```csharp
    private static byte[] BuildPng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static HttpRequestMessage BuildUploadRequest(string url, string token, byte[] bytes, string fileName, string contentType)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        request.Content = content;
        return request;
    }

    [Fact]
    public async Task UploadThenGetPoster_RoundTrips()
    {
        var (_, adminToken) = await CreateAdminAsync();
        var createResponse = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), adminToken));
        var eventId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var uploadResponse = await _client.SendAsync(
            BuildUploadRequest($"/api/events/{eventId}/poster", adminToken, BuildPng(200, 100), "poster.png", "image/png"));
        Assert.Equal(HttpStatusCode.NoContent, uploadResponse.StatusCode);

        var memberToken = await RegisterMemberAsync();
        var getResponse = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/events/{eventId}/poster").WithBearer(memberToken));

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal("image/jpeg", getResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UploadPoster_NonAdmin_Forbidden()
    {
        var (_, adminToken) = await CreateAdminAsync();
        var createResponse = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), adminToken));
        var eventId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var memberToken = await RegisterMemberAsync();

        var response = await _client.SendAsync(
            BuildUploadRequest($"/api/events/{eventId}/poster", memberToken, BuildPng(10, 10), "poster.png", "image/png"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetPoster_BeforeAnyUpload_ReturnsNotFound()
    {
        var (_, adminToken) = await CreateAdminAsync();
        var createResponse = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), adminToken));
        var eventId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var memberToken = await RegisterMemberAsync();

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/events/{eventId}/poster").WithBearer(memberToken));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
```

Add `using SkiaSharp;` to the top of `EventsControllerTests.cs` if not already present (it is not —
this file currently has no SkiaSharp reference).

- [x] **Step 6: Run the tests to verify they fail first (route doesn't exist without Task 3, and Task
      3 is already done at this point in the plan — so verify they pass directly)**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter FullyQualifiedName~EventsControllerTests`
Expected: PASS — `UploadThenGetPoster_RoundTrips`, `UploadPoster_NonAdmin_Forbidden`, and
`GetPoster_BeforeAnyUpload_ReturnsNotFound` all green, alongside every pre-existing test in the file.

- [x] **Step 7: Commit**

```bash
git add tests/PSMPE.Portal.Application.UnitTests/Payments/PaymentServiceTests.cs tests/PSMPE.Portal.WebAPI.IntegrationTests/Events/EventsControllerTests.cs
git commit -m "test: fix Fee->FeeOnsite/FeeOnline references, add poster upload integration coverage"
```

---

## 5. EF Core migration

**Files:**
- Create: `src/PSMPE.Portal.Infrastructure/Persistence/Migrations/<timestamp>_AlterEventsAddDetailFieldsAndPoster.cs` (scaffolded, not hand-written)
- Create (auto-generated alongside it): `..._AlterEventsAddDetailFieldsAndPoster.Designer.cs`
- Modify (auto-updated): `src/PSMPE.Portal.Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`

This is an **ALTER migration on the existing `Events`/`EventSessions` tables** — the tables themselves
already exist from `20260824054348_AddEventsAndCpdTracker`. Do not write the migration file by hand;
scaffold it with the EF CLI (which reads the entity/config changes from Tasks 1–3) and then hand-correct
one part of the generated `Up()`/`Down()` methods so the rename doesn't silently drop existing `Fee`
data.

- [x] **Step 1: Scaffold the migration**

Run:
```bash
dotnet ef migrations add AlterEventsAddDetailFieldsAndPoster \
  --project src/PSMPE.Portal.Infrastructure/PSMPE.Portal.Infrastructure.csproj \
  --startup-project src/PSMPE.Portal.WebAPI/PSMPE.Portal.WebAPI.csproj \
  --output-dir Persistence/Migrations
```
Expected: A new migration file is created containing, among other operations, something equivalent to
`DropColumn(name: "Fee", table: "Events")` followed by two `AddColumn<decimal>` calls for
`FeeOnsite`/`FeeOnline`, plus `AddColumn` calls for `Objectives`, `Type`, `Hours`, `CpdCodeOnsite`,
`CpdCodeOnline`, `PosterImageStorageKey` on `Events`, and one `AddColumn` for `Venue` on
`EventSessions`.

- [x] **Step 2: Hand-edit the generated migration's `Up()` method** to replace the auto-generated
      drop-and-add pair for `Fee`/`FeeOnsite` with a `RenameColumn`, so any pre-existing `Fee` value
      (e.g. from seeded dev data) survives as `FeeOnsite` instead of being silently reset to the
      default. Find the two lines that look like:

```csharp
            migrationBuilder.DropColumn(
                name: "Fee",
                table: "Events");
```
```csharp
            migrationBuilder.AddColumn<decimal>(
                name: "FeeOnsite",
                table: "Events",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
```

and replace both with a single rename (delete the `DropColumn` block entirely, and replace the
`AddColumn` block for `FeeOnsite` with):

```csharp
            migrationBuilder.RenameColumn(
                name: "Fee",
                table: "Events",
                newName: "FeeOnsite");
```

Leave the separate `AddColumn<decimal>` call for `FeeOnline` exactly as scaffolded (that's a genuinely
new column with no prior data to preserve, so `defaultValue: 0m` for existing rows is correct as-is).

- [x] **Step 3: Mirror the same fix in `Down()`** — find the reverse pair (an `AddColumn` for `Fee`
      alongside a `DropColumn` for `FeeOnsite`) and replace both with the reverse rename:

```csharp
            migrationBuilder.RenameColumn(
                name: "FeeOnsite",
                table: "Events",
                newName: "Fee");
```

(Keep `Down()`'s `DropColumn` for `FeeOnline` as scaffolded — reverting should drop the column that
never existed before this migration.)

- [x] **Step 4: Apply the migration to your local database and verify it runs cleanly**

Run: `dotnet ef database update --project src/PSMPE.Portal.Infrastructure/PSMPE.Portal.Infrastructure.csproj --startup-project src/PSMPE.Portal.WebAPI/PSMPE.Portal.WebAPI.csproj`
Expected: Migration applies with no errors. If you have any pre-existing `Events` rows from local
testing, spot-check one: its old `Fee` value should now appear under `FeeOnsite`, and `FeeOnline`
should read `0`.

- [x] **Step 5: Run the full backend test suite** (integration tests spin up their own database via
      `WebApplicationFactory` and apply migrations fresh, so this also validates the migration from a
      clean slate)

Run: `dotnet test`
Expected: All tests pass, including everything fixed/added in Tasks 2–4.

- [x] **Step 6: Commit**

```bash
git add src/PSMPE.Portal.Infrastructure/Persistence/Migrations/
git commit -m "feat: add AlterEventsAddDetailFieldsAndPoster migration (FeeOnsite/FeeOnline rename, new detail fields, session Venue)"
```

---

## 6. Frontend: `eventApi.ts` types and new endpoints

**Files:**
- Modify: `apps/web/src/core/api/endpoints/eventApi.ts`

- [x] **Step 1: Modify `eventApi.ts`** — full replacement content:

```typescript
import { apiClient } from '../apiClient'
import type { PagedResult } from './adminApi'

export const EventMode = {
  Onsite: 'Onsite',
  Online: 'Online',
} as const
export type EventModeValue = (typeof EventMode)[keyof typeof EventMode]

export const EventRegistrationStatus = {
  Registered: 'Registered',
  PaymentSubmitted: 'PaymentSubmitted',
  PaymentVerified: 'PaymentVerified',
  Attended: 'Attended',
  EvaluationSubmitted: 'EvaluationSubmitted',
  Rejected: 'Rejected',
  Cancelled: 'Cancelled',
} as const
export type EventRegistrationStatusValue = (typeof EventRegistrationStatus)[keyof typeof EventRegistrationStatus]

/** Mirrors EventTypes.cs. Free text against this list, not a validated backend enum - see
 *  Event.Type's backend doc comment. */
export const EventTypes = {
  Conference: 'Conference',
  Seminar: 'Seminar',
  Technoforum: 'Technoforum',
  Convention: 'Convention',
  Symposium: 'Symposium',
  Expo: 'Expo',
} as const
export type EventTypeValue = (typeof EventTypes)[keyof typeof EventTypes]

export interface EventSession {
  id: string
  title: string
  startsAt: string
  endsAt: string
  order: number
  /** Raw override - null means "no override, falls back to the parent Event's venue." Compute the
   *  effective venue as `session.venue ?? event.venue` at display time. */
  venue: string | null
}

export interface EventSessionInput {
  id: string | null
  title: string
  startsAt: string
  endsAt: string
  order: number
  venue: string | null
}

export interface Event {
  id: string
  title: string
  description: string | null
  objectives: string | null
  type: string | null
  chapter: string | null
  venue: string | null
  startsAt: string
  endsAt: string
  hours: number | null
  capacity: number | null
  registeredCount: number
  feeOnsite: number
  feeOnline: number
  /** Null means "TBD" - see Event.CpdUnitsOnsite's backend doc comment. */
  cpdUnitsOnsite: number | null
  cpdUnitsOnline: number | null
  cpdCodeOnsite: string | null
  cpdCodeOnline: string | null
  hasPoster: boolean
  sessions: EventSession[]
}

export interface CreateEventRequest {
  title: string
  description: string | null
  chapter: string | null
  venue: string | null
  startsAt: string
  endsAt: string
  capacity: number | null
  feeOnsite: number
  feeOnline: number
  type: string | null
  hours: number | null
  objectives: string | null
}

export interface UpdateEventRequest extends CreateEventRequest {
  cpdUnitsOnsite: number | null
  cpdUnitsOnline: number | null
  cpdCodeOnsite: string | null
  cpdCodeOnline: string | null
  sessions: EventSessionInput[]
}

export interface EventRegistration {
  id: string
  eventId: string
  eventTitle: string
  eventStartsAt: string
  memberId: string
  memberName: string
  membershipNo: string | null
  mode: EventModeValue
  status: EventRegistrationStatusValue
  sessionsAttended: number
  totalSessions: number
  evaluationRating: number | null
  evaluationComments: string | null
  evaluationSubmittedAt: string | null
  creditUnits: number | null
}

export interface EventRosterEntry {
  registrationId: string
  memberId: string
  memberName: string
  membershipNo: string | null
  mode: EventModeValue
  status: EventRegistrationStatusValue
  attendedSessionIds: string[]
  totalSessions: number
  paymentId: string | null
  paymentStatus: string | null
  paymentIsCash: boolean | null
  paymentRejectedReason: string | null
  evaluationRating: number | null
  evaluationSubmittedAt: string | null
  creditUnits: number | null
}

export interface EventRoster {
  eventId: string
  eventTitle: string
  sessions: EventSession[]
  registrants: EventRosterEntry[]
}

export interface MyCpdRegistration {
  registrationId: string
  eventId: string
  eventTitle: string
  eventStartsAt: string
  mode: EventModeValue
  status: EventRegistrationStatusValue
  sessionsAttended: number
  totalSessions: number
  creditUnits: number | null
}

export interface MyCpdSummary {
  totalCreditUnits: number
  registrations: MyCpdRegistration[]
}

export const eventApi = {
  getEvents: (params: { page?: number; pageSize?: number; search?: string; chapter?: string; upcomingOnly?: boolean } = {}) =>
    apiClient.get<PagedResult<Event>>('/api/events', { params }).then((res) => res.data),

  getEvent: (id: string) => apiClient.get<Event>(`/api/events/${id}`).then((res) => res.data),

  createEvent: (request: CreateEventRequest) => apiClient.post<Event>('/api/events', request).then((res) => res.data),

  updateEvent: (id: string, request: UpdateEventRequest) =>
    apiClient.put<Event>(`/api/events/${id}`, request).then((res) => res.data),

  /** Admin-only. Overwrites any previous poster - an event has exactly one. */
  uploadPoster: (eventId: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return apiClient.post(`/api/events/${eventId}/poster`, form).then((res) => res.data)
  },

  /** Fetched as a blob, same reasoning as downloadCertificate below - an authenticated image can't
   *  be a plain <img src>. Returns null if the event has no poster yet or the request fails. */
  getPosterUrl: async (eventId: string): Promise<string | null> => {
    try {
      const response = await apiClient.get(`/api/events/${eventId}/poster`, { responseType: 'blob' })
      return URL.createObjectURL(response.data)
    } catch {
      return null
    }
  },

  register: (eventId: string, mode: EventModeValue) =>
    apiClient.post<EventRegistration>(`/api/events/${eventId}/register`, { mode }).then((res) => res.data),

  cancelRegistration: (registrationId: string) =>
    apiClient.post(`/api/events/registrations/${registrationId}/cancel`).then((res) => res.data),

  submitPayment: (registrationId: string, request: { amount: number; referenceNo: string | null; paidOn: string }) =>
    apiClient.post(`/api/events/registrations/${registrationId}/payment`, request).then((res) => res.data),

  /** Attaches proof to the payment just created by submitPayment - reuses the existing generic
   *  payment-proof endpoint, since a Payment's proof isn't tied to which Kind it is. */
  uploadPaymentProof: (paymentId: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return apiClient.post(`/api/payments/${paymentId}/proof`, form).then((res) => res.data)
  },

  recordCashPayment: (registrationId: string, amount: number) =>
    apiClient.post(`/api/events/registrations/${registrationId}/payment/cash`, { amount }).then((res) => res.data),

  recordAttendance: (eventId: string, registrants: { registrationId: string; sessionIds: string[] }[]) =>
    apiClient.post(`/api/events/${eventId}/roster/attendance`, { registrants }).then((res) => res.data),

  submitEvaluation: (registrationId: string, rating: number, comments: string | null) =>
    apiClient.post(`/api/events/registrations/${registrationId}/evaluation`, { rating, comments }).then((res) => res.data),

  getRoster: (eventId: string) => apiClient.get<EventRoster>(`/api/events/${eventId}/roster`).then((res) => res.data),

  getMyCpd: () => apiClient.get<MyCpdSummary>('/api/members/me/cpd').then((res) => res.data),

  /** Fetched as a blob, same reasoning as paymentApi.fetchProofUrl - an authenticated download
   *  can't be a plain <a href>. */
  downloadCertificate: async (registrationId: string): Promise<{ url: string } | null> => {
    try {
      const response = await apiClient.get(`/api/events/registrations/${registrationId}/certificate`, { responseType: 'blob' })
      return { url: URL.createObjectURL(response.data) }
    } catch {
      return null
    }
  },
}
```

- [x] **Step 2: Verify TypeScript compiles** (it will not yet — the three page components still use
      the old shape; Tasks 7–9 fix them)

Run: `cd apps/web && npx tsc -b`
Expected: Errors only in `EventFormModal.tsx`, `EventRegisterModal.tsx`, and `EventsTable.tsx` (all
fixed in Tasks 7–9).

- [x] **Step 3: Commit**

```bash
git add apps/web/src/core/api/endpoints/eventApi.ts
git commit -m "feat: add FeeOnsite/FeeOnline, Type, Hours, Objectives, CpdCode, poster, and session Venue to the events API client"
```

---

## 7. Frontend: `EventFormModal.tsx` — new admin fields and poster upload

**Files:**
- Modify: `apps/web/src/integrations/template/pages/EventFormModal.tsx`

- [x] **Step 1: Modify `EventFormModal.tsx`** — full replacement content:

```tsx
import { useEffect, useState } from 'react'
import type { Event, EventSessionInput } from '../../../core/api/endpoints/eventApi'
import { EventTypes, eventApi } from '../../../core/api/endpoints/eventApi'
import { Chapters } from '../../../core/types/member'
import { describeError } from '../../../core/utils/apiError'
import { StandardButton } from '../components/shared/StandardButton'

interface EventFormModalProps {
  event: Event | null
  mode: 'create' | 'edit'
  onClose: () => void
  onSaved: () => void
}

function toSessionInputs(event: Event | null): EventSessionInput[] {
  return event?.sessions.map((s) => ({ id: s.id, title: s.title, startsAt: s.startsAt, endsAt: s.endsAt, order: s.order, venue: s.venue })) ?? []
}

/** Admin-only event create/edit, including session (lecture) management, each modality's fee/CPD
 *  units/accreditation code, the poster image, and the descriptive fields (Type, Hours, Objectives) -
 *  see EventService.UpdateAsync's session reconciliation on the backend. */
export function EventFormModal({ event, mode, onClose, onSaved }: EventFormModalProps) {
  const [title, setTitle] = useState(event?.title ?? '')
  const [description, setDescription] = useState(event?.description ?? '')
  const [objectives, setObjectives] = useState(event?.objectives ?? '')
  const [type, setType] = useState(event?.type ?? '')
  const [chapter, setChapter] = useState(event?.chapter ?? '')
  const [venue, setVenue] = useState(event?.venue ?? '')
  const [startsAt, setStartsAt] = useState(event?.startsAt.slice(0, 16) ?? '')
  const [endsAt, setEndsAt] = useState(event?.endsAt.slice(0, 16) ?? '')
  const [hours, setHours] = useState(event?.hours?.toString() ?? '')
  const [capacity, setCapacity] = useState(event?.capacity?.toString() ?? '')
  const [feeOnsite, setFeeOnsite] = useState(event?.feeOnsite.toString() ?? '0')
  const [feeOnline, setFeeOnline] = useState(event?.feeOnline.toString() ?? '0')
  const [cpdUnitsOnsite, setCpdUnitsOnsite] = useState(event?.cpdUnitsOnsite?.toString() ?? '')
  const [cpdUnitsOnline, setCpdUnitsOnline] = useState(event?.cpdUnitsOnline?.toString() ?? '')
  const [cpdCodeOnsite, setCpdCodeOnsite] = useState(event?.cpdCodeOnsite ?? '')
  const [cpdCodeOnline, setCpdCodeOnline] = useState(event?.cpdCodeOnline ?? '')
  const [sessions, setSessions] = useState<EventSessionInput[]>(toSessionInputs(event))
  const [posterFile, setPosterFile] = useState<File | null>(null)
  const [posterPreviewUrl, setPosterPreviewUrl] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setSessions(toSessionInputs(event))
  }, [event])

  // Loads the existing poster (if any) for preview when editing - a freshly-chosen posterFile
  // (handled by handlePosterFileChange below) takes priority over this fetched preview.
  useEffect(() => {
    if (!event?.hasPoster) return
    let cancelled = false
    eventApi.getPosterUrl(event.id).then((url) => {
      if (!cancelled) setPosterPreviewUrl(url)
    })
    return () => {
      cancelled = true
    }
  }, [event])

  // Same Escape-to-close/backdrop-click shell as ConfirmationModal, LogDetailsModal, etc.
  useEffect(() => {
    const handleKeyDown = (keyEvent: KeyboardEvent) => {
      if (keyEvent.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  const handlePosterFileChange = (file: File | null) => {
    setPosterFile(file)
    if (file) setPosterPreviewUrl(URL.createObjectURL(file))
  }

  const updateSession = (index: number, patch: Partial<EventSessionInput>) => {
    setSessions((prev) => prev.map((s, i) => (i === index ? { ...s, ...patch } : s)))
  }

  const addSession = () => {
    setSessions((prev) => [...prev, { id: null, title: '', startsAt, endsAt, order: prev.length + 1, venue: null }])
  }

  const removeSession = (index: number) => {
    setSessions((prev) => prev.filter((_, i) => i !== index))
  }

  const handleSubmit = async () => {
    setSaving(true)
    setError(null)
    try {
      const basePayload = {
        title,
        description: description || null,
        chapter: chapter || null,
        venue: venue || null,
        startsAt: new Date(startsAt).toISOString(),
        endsAt: new Date(endsAt).toISOString(),
        capacity: capacity ? Number(capacity) : null,
        feeOnsite: Number(feeOnsite),
        feeOnline: Number(feeOnline),
        type: type || null,
        hours: hours ? Number(hours) : null,
        objectives: objectives || null,
      }

      let savedEventId = event?.id ?? null
      if (mode === 'create') {
        const created = await eventApi.createEvent(basePayload)
        savedEventId = created.id
      } else if (event) {
        await eventApi.updateEvent(event.id, {
          ...basePayload,
          cpdUnitsOnsite: cpdUnitsOnsite ? Number(cpdUnitsOnsite) : null,
          cpdUnitsOnline: cpdUnitsOnline ? Number(cpdUnitsOnline) : null,
          cpdCodeOnsite: cpdCodeOnsite || null,
          cpdCodeOnline: cpdCodeOnline || null,
          sessions,
        })
      }

      if (posterFile && savedEventId) {
        await eventApi.uploadPoster(savedEventId, posterFile)
      }

      onSaved()
    } catch (err) {
      setError(describeError(err, 'Could not save this event.'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-100 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/50" onClick={onClose} />
      <div className="relative card w-full max-w-2xl max-h-[90vh] overflow-y-auto">
        <div className="card-header">
          <h6 className="card-title">{mode === 'create' ? 'New Event' : 'Edit Event'}</h6>
        </div>
        <div className="card-body flex flex-col gap-3">
          {error && <p className="text-sm text-danger">{error}</p>}
          <input className="form-input" placeholder="Title" value={title} onChange={(e) => setTitle(e.target.value)} />
          <textarea
            className="form-input"
            placeholder="Description"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
          <textarea
            className="form-input"
            placeholder="Objectives"
            value={objectives}
            onChange={(e) => setObjectives(e.target.value)}
          />
          <div className="grid grid-cols-2 gap-3">
            <select className="form-input" value={type} onChange={(e) => setType(e.target.value)}>
              <option value="">No type set</option>
              {Object.values(EventTypes).map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
            <input
              type="number"
              step="0.01"
              className="form-input"
              placeholder="Hours (PRC-declared)"
              value={hours}
              onChange={(e) => setHours(e.target.value)}
            />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <select className="form-input" value={chapter} onChange={(e) => setChapter(e.target.value)}>
              <option value="">National (all chapters)</option>
              {Object.values(Chapters).map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
            <input className="form-input" placeholder="Venue" value={venue} onChange={(e) => setVenue(e.target.value)} />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <input type="datetime-local" className="form-input" value={startsAt} onChange={(e) => setStartsAt(e.target.value)} />
            <input type="datetime-local" className="form-input" value={endsAt} onChange={(e) => setEndsAt(e.target.value)} />
          </div>
          <div className="grid grid-cols-3 gap-3">
            <input
              type="number"
              className="form-input"
              placeholder="Capacity"
              value={capacity}
              onChange={(e) => setCapacity(e.target.value)}
            />
            <input
              type="number"
              className="form-input"
              placeholder="Fee (Onsite)"
              value={feeOnsite}
              onChange={(e) => setFeeOnsite(e.target.value)}
            />
            <input
              type="number"
              className="form-input"
              placeholder="Fee (Online)"
              value={feeOnline}
              onChange={(e) => setFeeOnline(e.target.value)}
            />
          </div>

          <div>
            <label className="text-sm text-default-600 block mb-1">Poster / banner image</label>
            {posterPreviewUrl && (
              <img src={posterPreviewUrl} alt="Poster preview" className="w-full h-32 object-cover rounded-md mb-2" />
            )}
            <input
              type="file"
              accept="image/jpeg,image/png"
              className="text-sm"
              onChange={(e) => handlePosterFileChange(e.target.files?.[0] ?? null)}
            />
          </div>

          {mode === 'edit' && (
            <>
              <div className="grid grid-cols-2 gap-3">
                <input
                  type="number"
                  step="0.01"
                  className="form-input"
                  placeholder="CPD Units (Onsite) - blank for TBD"
                  value={cpdUnitsOnsite}
                  onChange={(e) => setCpdUnitsOnsite(e.target.value)}
                />
                <input
                  type="number"
                  step="0.01"
                  className="form-input"
                  placeholder="CPD Units (Online) - blank for TBD"
                  value={cpdUnitsOnline}
                  onChange={(e) => setCpdUnitsOnline(e.target.value)}
                />
              </div>
              <div className="grid grid-cols-2 gap-3">
                <input
                  className="form-input"
                  placeholder="PRC Accreditation Code (Onsite)"
                  value={cpdCodeOnsite}
                  onChange={(e) => setCpdCodeOnsite(e.target.value)}
                />
                <input
                  className="form-input"
                  placeholder="PRC Accreditation Code (Online)"
                  value={cpdCodeOnline}
                  onChange={(e) => setCpdCodeOnline(e.target.value)}
                />
              </div>

              <div className="border-t border-default-200 pt-3">
                <div className="flex items-center justify-between mb-2">
                  <h6 className="text-sm font-semibold">Sessions / Lectures</h6>
                  <StandardButton variant="secondary" size="sm" onClick={addSession}>
                    Add session
                  </StandardButton>
                </div>
                {sessions.map((session, index) => (
                  <div key={session.id ?? `new-${index}`} className="grid grid-cols-[1fr_auto_auto_1fr_auto] gap-2 mb-2 items-center">
                    <input
                      className="form-input"
                      placeholder="Session title"
                      value={session.title}
                      onChange={(e) => updateSession(index, { title: e.target.value })}
                    />
                    <input
                      type="datetime-local"
                      className="form-input"
                      value={session.startsAt.slice(0, 16)}
                      onChange={(e) => updateSession(index, { startsAt: new Date(e.target.value).toISOString() })}
                    />
                    <input
                      type="datetime-local"
                      className="form-input"
                      value={session.endsAt.slice(0, 16)}
                      onChange={(e) => updateSession(index, { endsAt: new Date(e.target.value).toISOString() })}
                    />
                    <input
                      className="form-input"
                      placeholder="Venue override (blank = event's venue)"
                      value={session.venue ?? ''}
                      onChange={(e) => updateSession(index, { venue: e.target.value || null })}
                    />
                    <StandardButton variant="danger" size="sm" onClick={() => removeSession(index)}>
                      Remove
                    </StandardButton>
                  </div>
                ))}
              </div>
            </>
          )}
        </div>
        <div className="card-footer flex justify-end gap-2">
          <StandardButton variant="secondary" onClick={onClose} disabled={saving}>
            Cancel
          </StandardButton>
          <StandardButton onClick={handleSubmit} loading={saving} loadingLabel="Saving…">
            Save
          </StandardButton>
        </div>
      </div>
    </div>
  )
}
```

- [x] **Step 2: Verify this file's slice of the TypeScript build is clean** (other files still break
      until Tasks 8–9)

Run: `cd apps/web && npx tsc -b --noEmit 2>&1 | grep EventFormModal || echo "no errors in EventFormModal.tsx"`
Expected: `no errors in EventFormModal.tsx`

- [x] **Step 3: Commit**

```bash
git add apps/web/src/integrations/template/pages/EventFormModal.tsx
git commit -m "feat: add Type, Hours, Objectives, per-modality fee/CPD code fields, poster upload, and session Venue to the admin event form"
```

---

## 8. Frontend: `EventRegisterModal.tsx` — live per-modality fee/CPD display and event detail

**Files:**
- Modify: `apps/web/src/integrations/template/pages/EventRegisterModal.tsx`

- [x] **Step 1: Modify `EventRegisterModal.tsx`** — full replacement content:

```tsx
import { useEffect, useState } from 'react'
import type { Event } from '../../../core/api/endpoints/eventApi'
import { EventMode, type EventModeValue, eventApi } from '../../../core/api/endpoints/eventApi'
import { describeError } from '../../../core/utils/apiError'
import { StandardButton } from '../components/shared/StandardButton'

interface EventRegisterModalProps {
  event: Event
  onClose: () => void
  onRegistered: () => void
}

function feeForMode(event: Event, mode: EventModeValue): number {
  return mode === EventMode.Onsite ? event.feeOnsite : event.feeOnline
}

/** Member-facing: shows the event's detail (poster, type, hours, objectives, sessions with their
 *  effective venue), lets the member pick a modality (fee and CPD units update live for whichever
 *  is selected), registers, then optionally submits payment proof right away (the member can also
 *  come back to it later from My CPD - registering alone is enough to hold the Registered row). */
export function EventRegisterModal({ event, onClose, onRegistered }: EventRegisterModalProps) {
  const [mode, setMode] = useState<EventModeValue>(EventMode.Onsite)
  const [amount, setAmount] = useState(feeForMode(event, EventMode.Onsite).toString())
  const [referenceNo, setReferenceNo] = useState('')
  const [paidOn, setPaidOn] = useState(new Date().toISOString().slice(0, 10))
  const [proofFile, setProofFile] = useState<File | null>(null)
  const [registrationId, setRegistrationId] = useState<string | null>(null)
  const [posterUrl, setPosterUrl] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!event.hasPoster) return
    let cancelled = false
    eventApi.getPosterUrl(event.id).then((url) => {
      if (!cancelled) setPosterUrl(url)
    })
    return () => {
      cancelled = true
    }
  }, [event.id, event.hasPoster])

  // Keeps the amount field in sync with whichever modality is currently selected, but only before
  // the member has registered - once registrationId is set, the amount field becomes the member's
  // own editable payment declaration and should stop tracking the radio selection.
  useEffect(() => {
    if (!registrationId) {
      setAmount(feeForMode(event, mode).toString())
    }
  }, [event, mode, registrationId])

  // Same Escape-to-close/backdrop-click shell as ConfirmationModal, LogDetailsModal, etc.
  useEffect(() => {
    const handleKeyDown = (keyEvent: KeyboardEvent) => {
      if (keyEvent.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  const handleRegister = async () => {
    setSaving(true)
    setError(null)
    try {
      const registration = await eventApi.register(event.id, mode)
      setRegistrationId(registration.id)
      if (feeForMode(event, mode) <= 0) {
        onRegistered()
      }
    } catch (err) {
      setError(describeError(err, 'Could not register for this event.'))
    } finally {
      setSaving(false)
    }
  }

  const handleSubmitPayment = async () => {
    if (!registrationId) return
    setSaving(true)
    setError(null)
    try {
      const payment = await eventApi.submitPayment(registrationId, { amount: Number(amount), referenceNo: referenceNo || null, paidOn })
      if (proofFile) {
        await eventApi.uploadPaymentProof(payment.id, proofFile)
      }
      onRegistered()
    } catch (err) {
      setError(describeError(err, 'Could not submit your payment.'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-100 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/50" onClick={onClose} />
      <div className="relative card w-full max-w-md max-h-[90vh] overflow-y-auto">
        <div className="card-header">
          <h6 className="card-title">Register for {event.title}</h6>
        </div>
        <div className="card-body flex flex-col gap-3">
          {error && <p className="text-sm text-danger">{error}</p>}

          {posterUrl && <img src={posterUrl} alt={event.title} className="w-full h-32 object-cover rounded-md" />}
          {event.type && <p className="text-xs text-default-500">{event.type}</p>}
          {event.hours !== null && <p className="text-xs text-default-500">{event.hours} PRC hour(s)</p>}
          {event.objectives && <p className="text-sm text-default-600">{event.objectives}</p>}
          {event.sessions.length > 0 && (
            <div className="text-xs text-default-500 flex flex-col gap-0.5">
              {event.sessions.map((s) => (
                <div key={s.id}>
                  {s.title} — {s.venue ?? event.venue ?? 'Venue TBA'}
                </div>
              ))}
            </div>
          )}

          {!registrationId ? (
            <>
              <label className="flex items-center gap-2 text-sm">
                <input type="radio" name="eventMode" className="form-radio" checked={mode === EventMode.Onsite} onChange={() => setMode(EventMode.Onsite)} />
                Onsite {event.cpdUnitsOnsite !== null ? `(${event.cpdUnitsOnsite} CPD units${event.cpdCodeOnsite ? `, ${event.cpdCodeOnsite}` : ''})` : '(CPD units: TBD)'}
              </label>
              <label className="flex items-center gap-2 text-sm">
                <input type="radio" name="eventMode" className="form-radio" checked={mode === EventMode.Online} onChange={() => setMode(EventMode.Online)} />
                Online {event.cpdUnitsOnline !== null ? `(${event.cpdUnitsOnline} CPD units${event.cpdCodeOnline ? `, ${event.cpdCodeOnline}` : ''})` : '(CPD units: TBD)'}
              </label>
              <p className="text-sm text-default-600">
                Fee: {feeForMode(event, mode) > 0 ? `PHP ${feeForMode(event, mode).toFixed(2)}` : 'Free'}
              </p>
            </>
          ) : (
            <>
              <p className="text-sm text-default-600">You're registered. Submit your payment proof to move to verification:</p>
              <input
                type="number"
                min="0"
                step="0.01"
                className="form-input"
                placeholder="Amount"
                value={amount}
                onChange={(e) => setAmount(e.target.value)}
              />
              <input className="form-input" placeholder="Reference No." value={referenceNo} onChange={(e) => setReferenceNo(e.target.value)} />
              <input
                type="date"
                className="form-input"
                max={new Date().toISOString().slice(0, 10)}
                value={paidOn}
                onChange={(e) => setPaidOn(e.target.value)}
              />
              <input type="file" accept="image/*,.pdf" className="text-sm" onChange={(e) => setProofFile(e.target.files?.[0] ?? null)} />
            </>
          )}
        </div>
        <div className="card-footer flex justify-end gap-2">
          <StandardButton variant="secondary" onClick={onClose} disabled={saving}>
            Cancel
          </StandardButton>
          {!registrationId ? (
            <StandardButton onClick={handleRegister} loading={saving} loadingLabel="Registering…">
              Register
            </StandardButton>
          ) : (
            <StandardButton onClick={handleSubmitPayment} loading={saving} loadingLabel="Submitting…">
              Submit Payment
            </StandardButton>
          )}
        </div>
      </div>
    </div>
  )
}
```

- [x] **Step 2: Verify this file's slice of the TypeScript build is clean**

Run: `cd apps/web && npx tsc -b --noEmit 2>&1 | grep EventRegisterModal || echo "no errors in EventRegisterModal.tsx"`
Expected: `no errors in EventRegisterModal.tsx`

- [x] **Step 3: Commit**

```bash
git add apps/web/src/integrations/template/pages/EventRegisterModal.tsx
git commit -m "feat: show poster/type/hours/objectives/session venues and live per-modality fee/CPD units in the register modal"
```

---

## 9. Frontend: `EventsTable.tsx` — split fee display

**Files:**
- Modify: `apps/web/src/integrations/template/pages/EventsTable.tsx`

- [x] **Step 1: Modify `EventsTable.tsx`** — replace the single `event.fee` display (around the
      existing `<p className="text-sm font-semibold">` line) with the two-modality version. Find:

```tsx
                  <div className="text-right shrink-0">
                    <p className="text-sm font-semibold">{event.fee > 0 ? `PHP ${event.fee.toFixed(2)}` : 'Free'}</p>
                    <p className="text-xs text-default-500">
```

Replace with:

```tsx
                  <div className="text-right shrink-0">
                    <p className="text-sm font-semibold">
                      {event.feeOnsite > 0 || event.feeOnline > 0
                        ? `Onsite PHP ${event.feeOnsite.toFixed(2)} / Online PHP ${event.feeOnline.toFixed(2)}`
                        : 'Free'}
                    </p>
                    <p className="text-xs text-default-500">
```

- [x] **Step 2: Verify this file's slice of the TypeScript build is clean, and the whole frontend
      build now compiles end to end**

Run: `cd apps/web && npx tsc -b`
Expected: Build succeeds with zero errors (this is the last of the three page components with a
stale `Event.fee` reference).

- [x] **Step 3: Commit**

```bash
git add apps/web/src/integrations/template/pages/EventsTable.tsx
git commit -m "feat: show FeeOnsite/FeeOnline separately in the events list"
```

---

## 10. Documentation and final verification

**Files:**
- Modify: `openspecs/events.md`

- [x] **Step 1: Modify `openspecs/events.md`** — update "The `Event` → `EventSession` →
      `EventAttendance` shape" section's `Event`/`EventSession` bullet points to reflect the new
      fields. Find:

```markdown
- **`Event`** — `Title`, `Description`, `Chapter` (null for a national/all-chapters event), `Venue`,
  `StartsAt`/`EndsAt`, `Capacity`, `Fee`, and the two independently-nullable `CpdUnitsOnsite`/
  `CpdUnitsOnline`.
- **`EventSession`** — one lecture/segment of a (possibly multi-day) event: `Title`, `StartsAt`/
  `EndsAt`, `Order` (display sequence only, not a uniqueness constraint). `EventService.CreateAsync`
  always creates at least one session — an event with no separate lectures still gets exactly one
  session spanning the whole event — so nothing downstream needs a special case for a
  single-session event.
```

Replace with:

```markdown
- **`Event`** — `Title`, `Description`, `Objectives` (same shape as `Description`), `Type` (free text
  against `EventTypes.All` — Conference, Seminar, Technoforum, Convention, Symposium, Expo, mirroring
  `Member.MemberType`/`MemberTypes`), `Chapter` (null for a national/all-chapters event), `Venue`,
  `StartsAt`/`EndsAt`, `Hours` (a single PRC-declared hour count shared across both modalities),
  `Capacity` (informational planning target only — never enforced, never blocks registration), the
  independently-settable `FeeOnsite`/`FeeOnline`, the two independently-nullable `CpdUnitsOnsite`/
  `CpdUnitsOnline`, their PRC accreditation references `CpdCodeOnsite`/`CpdCodeOnline` (also
  independently nullable, informational only, never validated against PRC), and
  `PosterImageStorageKey` (an admin-uploaded banner image, set only via `EventPosterService` — see
  "The poster image" below).
- **`EventSession`** — one lecture/segment of a (possibly multi-day) event: `Title`, `StartsAt`/
  `EndsAt`, `Order` (display sequence only, not a uniqueness constraint), and `Venue` — an optional
  override for this session's display venue; falls back to `Event.Venue` when null (e.g. for a
  multi-city or multi-room event where one lecture happens somewhere different from the rest).
  `EventService.CreateAsync` always creates at least one session — an event with no separate lectures
  still gets exactly one session spanning the whole event — so nothing downstream needs a special
  case for a single-session event.

## The poster image

An Admin can attach a JPG/PNG banner image via `POST /api/events/{id}/poster` (multipart form,
`events:manage`), which `EventPosterService` validates (JPG/PNG only, 8 MB raw upload cap),
downscales to at most 1600px on the longest side, re-encodes as JPEG, and writes to
`Event.PosterImageStorageKey` — the same validate-downscale-reencode pipeline
`MemberUploadService` uses for Member Photo, but simpler: exactly one poster per event, stored
directly on the `Event` row rather than a separate join table. `GET /api/events/{id}/poster` streams
it back (any authenticated caller — the poster is shown on the member-facing events list and register
view, not just to staff). `EventDto.HasPoster` (derived from `PosterImageStorageKey is not null`, the
same pattern as `PaymentDto.HasProof`) tells the frontend whether to fetch it. Uploading again
overwrites the previous poster; there is no history.
```

- [x] **Step 2: Update the endpoint table** to add the two new poster rows immediately after the
      existing `PUT /api/events/{id}` row. Find:

```markdown
| `PUT /api/events/{id}` | `events:manage` | Edit event details, set/correct either CPD unit value, add/remove/reorder sessions | `404` unknown event; `400` invalid (no sessions left, `EndsAt` before `StartsAt`, a session id not belonging to this event); `409` removing a session that already has recorded attendance |
```

Replace with (adds two new table rows immediately below the unchanged original row):

```markdown
| `PUT /api/events/{id}` | `events:manage` | Edit event details, set/correct either CPD unit value, add/remove/reorder sessions | `404` unknown event; `400` invalid (no sessions left, `EndsAt` before `StartsAt`, a session id not belonging to this event); `409` removing a session that already has recorded attendance |
| `POST /api/events/{id}/poster` | `events:manage` | Upload/replace the event's poster/banner image (multipart) | `404` unknown event; `400` not a JPG/PNG, over 8 MB, or unreadable; `403` without the permission |
| `GET /api/events/{id}/poster` | Any authenticated | Stream the poster image | `404` unknown event or no poster uploaded yet |
```

- [x] **Step 3: Run the full backend and frontend verification suites one more time**

Run: `dotnet test`
Expected: All tests pass.

Run: `cd apps/web && npx tsc -b && npx eslint src`
Expected: Both succeed with zero errors.

- [x] **Step 4: Commit**

```bash
git add openspecs/events.md
git commit -m "docs: update events.md for FeeOnsite/FeeOnline, Type/Hours/Objectives, poster image, and session Venue"
```
