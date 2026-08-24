# Tasks: add-events-cpd-tracker

> **STALE — do not execute as-is.** `proposal.md` and `specs/events/spec.md` in this folder were
> revised 2026-08-24 against a stakeholder interview, and the design below no longer matches them:
> `Event.CpdUnits` is now two independent fields (`CpdUnitsOnsite`/`CpdUnitsOnline`), attendance is
> per-`EventSession` via admin roster reconciliation (not member self-check-in) with a new
> `EventAttendance` join entity, `EventRegistration` gained a `Mode` field, CPD credit is now
> prorated by sessions attended, and Payment integration gained an admin cash-payment path. Every
> task below touching `Event`/`EventRegistration` schema, attendance, CPD computation, payment, the
> roster screen, or certificate generation needs to be regenerated against the revised proposal/spec
> before this plan is executed — treat this file as a reference for task *structure and sequencing*
> only, not as accurate implementation steps.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build Event Management and the CPD Credit Tracker together — members register for a
PSMPE event (paid, via the existing Payments domain), self check-in on the day, submit a post-event
evaluation, and earn CPD credit computed from the event's unit count, which an admin can set before
or after the event. Admins manage events and rosters; members see a running credit total and can
download a certificate once credit is earned.

**Architecture:** Two new EF Core entities (`Event`, `EventRegistration`) behind a thin
`EventService`, mirroring the existing `Payment` entity's single-row-with-status-enum shape.
`Payment` gains a third `Kind` (`EventRegistration`) and a nullable `EventRegistrationId` FK; the
existing verify/reject/proof-upload endpoints are reused unchanged (ownership checks are already
generic), only `PaymentVerification.Apply` and `PaymentService.VerifyAsync`/`RejectAsync` grow a
branch for the new kind. CPD credit is a computed property, never stored — there is no scheduler in
this codebase for anything to write it. Certificates are generated on demand with QuestPDF, not
pre-rendered. Full context: `openspec/changes/add-events-cpd-tracker/proposal.md` and
`specs/events/spec.md` in this folder — **read both before starting**.

**Tech Stack:** .NET 8 + EF Core 8 (Npgsql in prod, EF InMemory in Application unit tests) for the
backend; React 19 + Vite + TypeScript + Tailwind for the frontend, plain axios (no react-query), no
frontend test runner (verification is `tsc -b` / `eslint` / manual browser pass). Backend: xUnit
unit tests (`PSMPE.Portal.Application.UnitTests`) and xUnit integration tests
(`PSMPE.Portal.WebAPI.IntegrationTests`, real HTTP via `WebApplicationFactory<Program>`).

**Sequencing:** Tasks 1–10 ship the backend end-to-end (data model → services → API), verified by
tests at each layer. Tasks 11–15 build the frontend on top of a working API. Task 16 is final
verification and docs.

**Design note carried through every task below:** the existing `PaymentDto.Status`/`Kind` are typed
as raw C# enums, which — since this codebase has no `JsonStringEnumConverter` configured anywhere
(confirmed by search) — actually serialize as **numbers** over the wire, even though
`paymentApi.ts` types them as string literals (`'Submitted'`, `'Verified'`, ...). That mismatch is a
pre-existing issue in `Payment`, out of scope to fix here. **Do not repeat it**: every new DTO field
below that represents `EventRegistrationStatus` is explicitly converted with `.ToString()` in the
mapping code, so it actually is the string the frontend types expect.

---

## 1. Domain entities and DbContext wiring

**Files:**
- Create: `src/PSMPE.Portal.Domain/Entities/Event.cs`
- Create: `src/PSMPE.Portal.Domain/Entities/EventRegistration.cs`
- Create: `src/PSMPE.Portal.Domain/Enums/EventRegistrationStatus.cs`
- Modify: `src/PSMPE.Portal.Domain/Enums/PaymentKind.cs`
- Modify: `src/PSMPE.Portal.Domain/Entities/Payment.cs`
- Modify: `src/PSMPE.Portal.Application/Common/Interfaces/IApplicationDbContext.cs`
- Modify: `src/PSMPE.Portal.Infrastructure/Persistence/ApplicationDbContext.cs`
- Modify: `tests/PSMPE.Portal.Application.UnitTests/TestSupport/TestDbContext.cs`

Pure data classes and DI plumbing — no meaningful behavior to TDD here; verification is a
successful build.

- [ ] **Step 1: Create the `EventRegistrationStatus` enum**

```csharp
namespace PSMPE.Portal.Domain.Enums;

/// <summary>
/// Walks forward: Registered -> PaymentSubmitted -> PaymentVerified -> Attended ->
/// EvaluationSubmitted, with Rejected/Cancelled as off-ramps. One EventRegistration row per member
/// per event carries this single status rather than separate registration/attendance/evaluation
/// tables - see add-events-cpd-tracker/proposal.md.
/// </summary>
public enum EventRegistrationStatus
{
    Registered,
    PaymentSubmitted,
    PaymentVerified,
    Attended,
    EvaluationSubmitted,
    Rejected,
    Cancelled,
}
```

- [ ] **Step 2: Create the `Event` entity**

```csharp
namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// A PSMPE event or workshop (national convention, chapter seminar, technical workshop). CpdUnits
/// starts null ("TBD") - the accredited unit count is often only confirmed close to or after the
/// session, and an admin can set or correct it at any time; registration, payment, attendance and
/// evaluation all work the same regardless of whether it's set yet. Chapter is null for a
/// national/all-chapters event.
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
    public int? CpdUnits { get; set; }
}
```

- [ ] **Step 3: Create the `EventRegistration` entity**

```csharp
namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// One row per member per event - registration, payment progress, attendance and evaluation all
/// live on this single row via Status, mirroring Payment's single-row-with-status-enum shape (see
/// add-events-cpd-tracker/proposal.md). CPD credit is deliberately NOT a field here - it's computed
/// from Status + Event.CpdUnits at read time (see Application/Events/CpdCredit.cs), so a CpdUnits
/// value set or corrected after the fact is instantly correct everywhere with no backfill.
/// </summary>
public class EventRegistration : BaseEntity
{
    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;

    public Guid MemberId { get; set; }
    public Member Member { get; set; } = null!;

    public EventRegistrationStatus Status { get; set; } = EventRegistrationStatus.Registered;

    /// <summary>Null when the member self-checked-in; set to the acting admin's user id when an
    /// admin set or corrected attendance instead - see the Attended/self-check-in scenarios in
    /// specs/events/spec.md.</summary>
    public DateTimeOffset? AttendedAt { get; set; }
    public Guid? AttendedByUserId { get; set; }

    /// <summary>1-5. Fixed field set, not admin-configurable per event, to keep this pass
    /// scoped - see proposal.md's "Not Built".</summary>
    public int? EvaluationRating { get; set; }
    public string? EvaluationComments { get; set; }
    public DateTimeOffset? EvaluationSubmittedAt { get; set; }
}
```

- [ ] **Step 4: Extend `PaymentKind` and `Payment` for event registrations**

In `src/PSMPE.Portal.Domain/Enums/PaymentKind.cs`, add a third case (safe to append - the enum is
stored as text via `HasConversion<string>()`, so there's no ordinal-shift risk for existing rows):

```csharp
public enum PaymentKind
{
    NewMembership,
    Renewal,
    EventRegistration,
}
```

In `src/PSMPE.Portal.Domain/Entities/Payment.cs`, add after the `MemberId`/`Member` properties:

```csharp
    /// <summary>Set only when Kind is EventRegistration. Nullable because NewMembership/Renewal
    /// payments have no event.</summary>
    public Guid? EventRegistrationId { get; set; }
    public EventRegistration? EventRegistration { get; set; }
```

- [ ] **Step 5: Add the two new `DbSet`s to `IApplicationDbContext`**

In `src/PSMPE.Portal.Application/Common/Interfaces/IApplicationDbContext.cs`, add after
`DbSet<Payment> Payments { get; }`:

```csharp
    DbSet<Event> Events { get; }
    DbSet<EventRegistration> EventRegistrations { get; }
```

- [ ] **Step 6: Add the two new `DbSet`s to `ApplicationDbContext`**

In `src/PSMPE.Portal.Infrastructure/Persistence/ApplicationDbContext.cs`, add after
`public DbSet<Payment> Payments => Set<Payment>();`:

```csharp
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventRegistration> EventRegistrations => Set<EventRegistration>();
```

- [ ] **Step 7: Add the two new `DbSet`s to `TestDbContext`**

In `tests/PSMPE.Portal.Application.UnitTests/TestSupport/TestDbContext.cs`, add the same two
properties (identical syntax to Step 6) so Application-layer unit tests can seed/assert against
both tables.

- [ ] **Step 8: Build to confirm everything compiles**

Run: `dotnet build src/PSMPE.Portal.sln`
Expected: build succeeds (0 errors). Both new entities are part of the EF model but have no table
yet — that's Task 2.

- [ ] **Step 9: Commit**

```bash
git add src/PSMPE.Portal.Domain/Entities/Event.cs src/PSMPE.Portal.Domain/Entities/EventRegistration.cs \
  src/PSMPE.Portal.Domain/Enums/EventRegistrationStatus.cs src/PSMPE.Portal.Domain/Enums/PaymentKind.cs \
  src/PSMPE.Portal.Domain/Entities/Payment.cs \
  src/PSMPE.Portal.Application/Common/Interfaces/IApplicationDbContext.cs \
  src/PSMPE.Portal.Infrastructure/Persistence/ApplicationDbContext.cs \
  tests/PSMPE.Portal.Application.UnitTests/TestSupport/TestDbContext.cs
git commit -m "feat: add Event and EventRegistration entities"
```

---

## 2. EF configurations and migration

**Files:**
- Create: `src/PSMPE.Portal.Infrastructure/Persistence/Configurations/EventConfiguration.cs`
- Create: `src/PSMPE.Portal.Infrastructure/Persistence/Configurations/EventRegistrationConfiguration.cs`
- Modify: `src/PSMPE.Portal.Infrastructure/Persistence/Configurations/PaymentConfiguration.cs`
- Create: a new migration under `src/PSMPE.Portal.Infrastructure/Persistence/Migrations`

- [ ] **Step 1: Create `EventConfiguration`**

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
        builder.Property(e => e.Chapter).HasMaxLength(64);
        builder.Property(e => e.Venue).HasMaxLength(256);
        builder.Property(e => e.Fee).HasPrecision(12, 2);

        // The events list filters/sorts on StartsAt; the admin roster looks events up by id only.
        builder.HasIndex(e => e.StartsAt);
    }
}
```

- [ ] **Step 2: Create `EventRegistrationConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class EventRegistrationConfiguration : IEntityTypeConfiguration<EventRegistration>
{
    public void Configure(EntityTypeBuilder<EventRegistration> builder)
    {
        builder.Property(r => r.EvaluationComments).HasMaxLength(2000);

        // Stored as text, matching Payment/MemberUpload's convention: an int ordinal silently
        // remaps every existing row if a value is ever inserted into the middle of the enum.
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);

        // The roster query filters on EventId; "one non-cancelled registration per member per
        // event" is enforced in EventService, not by a DB constraint, since Cancelled rows must
        // stay queryable without blocking a fresh registration.
        builder.HasIndex(r => r.EventId);
        builder.HasIndex(r => r.MemberId);

        // Restrict, matching Payment.MemberId - deleting an Event or a Member must not silently
        // take registration history with it.
        builder.HasOne(r => r.Event)
            .WithMany()
            .HasForeignKey(r => r.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Member)
            .WithMany()
            .HasForeignKey(r => r.MemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 3: Extend `PaymentConfiguration` for the new FK**

In `src/PSMPE.Portal.Infrastructure/Persistence/Configurations/PaymentConfiguration.cs`, add inside
`Configure`, after the existing `builder.HasOne(p => p.Member)...` block:

```csharp
        // Restrict, same reasoning as MemberId - a registration with payment history shouldn't
        // vanish out from under its payment row.
        builder.HasOne(p => p.EventRegistration)
            .WithMany()
            .HasForeignKey(p => p.EventRegistrationId)
            .OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 4: Build to confirm the configurations compile**

Run: `dotnet build src/PSMPE.Portal.sln`
Expected: build succeeds (0 errors).

- [ ] **Step 5: Add the migration**

```bash
dotnet ef migrations add AddEventsAndEventRegistrations \
  --project src/PSMPE.Portal.Infrastructure/PSMPE.Portal.Infrastructure.csproj \
  --startup-project src/PSMPE.Portal.WebAPI/PSMPE.Portal.WebAPI.csproj \
  --output-dir Persistence/Migrations
```

Expected: a new `Persistence/Migrations/<timestamp>_AddEventsAndEventRegistrations.cs` file is
generated. Open it and confirm it creates two tables (`Events`, `EventRegistrations`) and adds one
nullable `EventRegistrationId` column + FK to `Payments` — matching the two configurations above.
If the generated `Up()` looks materially different from that (e.g. it tries to touch unrelated
tables), stop and re-check Steps 1–3 before proceeding; don't hand-edit the migration to force it
to match.

- [ ] **Step 6: Verify against a running database**

Run: `docker compose up -d postgres` (if not already running), then start the API once with
`dotnet run --project src/PSMPE.Portal.WebAPI` and confirm the startup log shows the migration
applying cleanly (this app auto-migrates on startup when `Seed:Enabled` is true — see
`README.md`'s "Migrations and seeding" section). Stop the API afterward (`Ctrl+C`).
Expected: no migration errors in the log; `Events` and `EventRegistrations` tables exist in
Postgres, and `Payments` has a new nullable `EventRegistrationId` column.

- [ ] **Step 7: Commit**

```bash
git add src/PSMPE.Portal.Infrastructure/Persistence/Configurations/EventConfiguration.cs \
  src/PSMPE.Portal.Infrastructure/Persistence/Configurations/EventRegistrationConfiguration.cs \
  src/PSMPE.Portal.Infrastructure/Persistence/Configurations/PaymentConfiguration.cs \
  src/PSMPE.Portal.Infrastructure/Persistence/Migrations/
git commit -m "feat: add EF configurations and migration for events and registrations"
```

---

## 3. Permissions

**Files:**
- Modify: `src/PSMPE.Portal.Domain/Enums/Permissions.cs`
- Modify: `src/PSMPE.Portal.Infrastructure/Persistence/Seed/IdentitySeeder.cs`

- [ ] **Step 1: Add the `Events` permission group**

In `src/PSMPE.Portal.Domain/Enums/Permissions.cs`, add a new nested class (after `Members`) and
extend the `All` array:

```csharp
    public static class Events
    {
        public const string View = "events:view";
        public const string Manage = "events:manage";
    }
```

```csharp
    public static readonly string[] All =
    [
        Content.Create, Content.Update, Content.Delete, Content.ManageOthers,
        Layout.Create, Layout.Delete, Layout.DeleteSystem,
        Admin.ManageUsers, Admin.ManageRoles,
        Ai.UsePrompt,
        Members.View, Members.Manage, Members.Approve,
        Events.View, Events.Manage
    ];
```

- [ ] **Step 2: Grant defaults in `IdentitySeeder`**

In `src/PSMPE.Portal.Infrastructure/Persistence/Seed/IdentitySeeder.cs`, `Events.Manage` goes to
Admin only (event creation/management is Admin/staff-only per the proposal — Super Admin already
gets everything via `Permissions.All`); `Events.View` also goes to Manager, since Manager already
holds `Members.View` for the same "can see, can't act" role. Update the `DefaultRolePermissions`
dictionary:

```csharp
        [RoleNames.Admin] =
        [
            Permissions.Content.Create, Permissions.Content.Update, Permissions.Content.Delete, Permissions.Content.ManageOthers,
            Permissions.Layout.Create, Permissions.Layout.Delete,
            Permissions.Admin.ManageUsers,
            Permissions.Ai.UsePrompt,
            Permissions.Members.View, Permissions.Members.Manage,
            Permissions.Events.View, Permissions.Events.Manage
        ],
        [RoleNames.Manager] =
        [
            Permissions.Content.Create, Permissions.Content.Update, Permissions.Content.Delete,
            Permissions.Layout.Create,
            Permissions.Ai.UsePrompt,
            Permissions.Members.View,
            Permissions.Events.View
        ],
```

(Only the `Admin` and `Manager` entries change — `Accounts`, `Approval`, `Member` and `SuperAdmin`
are unmodified.)

- [ ] **Step 3: Build to confirm it compiles**

Run: `dotnet build src/PSMPE.Portal.sln`
Expected: build succeeds (0 errors). New permission grants only take effect for roles created
*after* this change on a fresh database — an existing dev database's roles keep whatever
permissions they already have (`IdentitySeeder` only seeds a role's permissions the moment the role
row itself is first created, never on top of one that already exists — see its own doc comment). If
testing locally against an existing dev database, grant `Events.Manage`/`Events.View` to the Admin
role by hand via `/admin/roles` once, or drop and reseed the database.

- [ ] **Step 4: Commit**

```bash
git add src/PSMPE.Portal.Domain/Enums/Permissions.cs src/PSMPE.Portal.Infrastructure/Persistence/Seed/IdentitySeeder.cs
git commit -m "feat: add Events.View and Events.Manage permissions"
```

---

## 4. Event CRUD (Application layer)

**Files:**
- Create: `src/PSMPE.Portal.Application/Events/Dtos/EventDto.cs`
- Create: `src/PSMPE.Portal.Application/Events/IEventService.cs`
- Create: `src/PSMPE.Portal.Application/Events/EventService.cs`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs`

- [ ] **Step 1: Create the `EventDto` and request records**

```csharp
namespace PSMPE.Portal.Application.Events.Dtos;

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
    int? CpdUnits);

/// <summary>CpdUnits is deliberately absent here - it starts null/"TBD" and is only ever set
/// through UpdateEventRequest, never at creation.</summary>
public record CreateEventRequest(
    string Title,
    string? Description,
    string? Chapter,
    string? Venue,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int? Capacity,
    decimal Fee);

public record UpdateEventRequest(
    string Title,
    string? Description,
    string? Chapter,
    string? Venue,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int? Capacity,
    decimal Fee,
    int? CpdUnits);
```

- [ ] **Step 2: Create `IEventService` with just the event-management members for now**

(Registration/attendance/evaluation/roster/CPD members are added in Tasks 5–6 — this interface
grows across this section of the plan rather than being fully declared up front, so each task's
tests compile against only what exists so far.)

```csharp
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Events.Dtos;

namespace PSMPE.Portal.Application.Events;

public interface IEventService
{
    Task<PagedResult<EventDto>> GetAllAsync(
        int page, int pageSize, string? search, string? chapter, bool upcomingOnly,
        CancellationToken cancellationToken = default);

    Task<EventDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<EventDto>> CreateAsync(CreateEventRequest request, CancellationToken cancellationToken = default);

    Task<Result<EventDto>> UpdateAsync(Guid id, UpdateEventRequest request, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Write the failing tests for validation and listing**

```csharp
using PSMPE.Portal.Application.Events;
using PSMPE.Portal.Application.Events.Dtos;
using PSMPE.Portal.Application.UnitTests.TestSupport;
using PSMPE.Portal.Domain.Entities;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Events;

public class EventServiceTests
{
    private static CreateEventRequest ValidCreateRequest(string title = "Water Sanitation Workshop") =>
        new(title, "Cross-connection control", "NCR", "PICC", DateTimeOffset.UtcNow.AddDays(10),
            DateTimeOffset.UtcNow.AddDays(10).AddHours(4), Capacity: 100, Fee: 500m);

    [Fact]
    public async Task CreateAsync_ValidRequest_StartsWithCpdUnitsNull()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);

        var result = await service.CreateAsync(ValidCreateRequest());

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.CpdUnits);
    }

    [Fact]
    public async Task CreateAsync_EndsAtBeforeStartsAt_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var starts = DateTimeOffset.UtcNow.AddDays(10);
        var request = ValidCreateRequest() with { StartsAt = starts, EndsAt = starts.AddHours(-1) };

        var result = await service.CreateAsync(request);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task CreateAsync_BlankTitle_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);

        var result = await service.CreateAsync(ValidCreateRequest() with { Title = "  " });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpdateAsync_SetsCpdUnits()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = await service.CreateAsync(ValidCreateRequest());
        var updateRequest = new UpdateEventRequest(
            created.Value!.Title, created.Value.Description, created.Value.Chapter, created.Value.Venue,
            created.Value.StartsAt, created.Value.EndsAt, created.Value.Capacity, created.Value.Fee, CpdUnits: 4);

        var result = await service.UpdateAsync(created.Value.Id, updateRequest);

        Assert.True(result.Succeeded);
        Assert.Equal(4, result.Value!.CpdUnits);
    }

    /// <summary>
    /// GetAllAsync's RegisteredCount must reflect non-cancelled registrations only - a cancelled
    /// slot must free up capacity in what admins see, matching the "one non-cancelled registration
    /// per member per event" rule the registration flow enforces (Task 5).
    /// </summary>
    [Fact]
    public async Task GetAllAsync_RegisteredCount_ExcludesCancelledRegistrations()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = await service.CreateAsync(ValidCreateRequest());
        var user = new ApplicationUser { UserName = "reg@example.com", Email = "reg@example.com" };
        var member = new Member { UserId = user.Id, User = user, FirstName = "A", LastName = "B", Chapter = "NCR" };
        db.Add(user);
        db.Members.Add(member);
        db.EventRegistrations.Add(new EventRegistration
        {
            EventId = created.Value!.Id, MemberId = member.Id,
            Status = Domain.Enums.EventRegistrationStatus.Cancelled,
        });
        await db.SaveChangesAsync();

        var page = await service.GetAllAsync(1, 20, search: null, chapter: null, upcomingOnly: false);

        Assert.Equal(0, page.Items.Single().RegisteredCount);
    }

    [Fact]
    public async Task GetAllAsync_SearchFiltersByTitle()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        await service.CreateAsync(ValidCreateRequest("National Convention"));
        await service.CreateAsync(ValidCreateRequest("Plumbing Code Seminar"));

        var page = await service.GetAllAsync(1, 20, search: "seminar", chapter: null, upcomingOnly: false);

        Assert.Equal(["Plumbing Code Seminar"], page.Items.Select(e => e.Title));
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter EventServiceTests`
Expected: FAIL to compile — `EventService` doesn't exist yet.

- [ ] **Step 5: Implement `EventService`**

```csharp
using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Events.Dtos;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Events;

public class EventService(IApplicationDbContext db) : IEventService
{
    public async Task<PagedResult<EventDto>> GetAllAsync(
        int page, int pageSize, string? search, string? chapter, bool upcomingOnly,
        CancellationToken cancellationToken = default)
    {
        var query = db.Events.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(e => EF.Functions.ILike(e.Title, $"%{term}%"));
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
        var @event = await db.Events.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
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
        var validation = Validate(request.Title, request.StartsAt, request.EndsAt, request.Capacity, request.Fee, request.Chapter);
        if (validation is not null)
        {
            return Result<EventDto>.Failure(validation);
        }

        var @event = new Event
        {
            Title = request.Title.Trim(),
            Description = request.Description,
            Chapter = request.Chapter,
            Venue = request.Venue,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            Capacity = request.Capacity,
            Fee = request.Fee,
        };
        db.Events.Add(@event);
        await db.SaveChangesAsync(cancellationToken);

        return Result<EventDto>.Success(ToDto(@event, registeredCount: 0));
    }

    public async Task<Result<EventDto>> UpdateAsync(Guid id, UpdateEventRequest request, CancellationToken cancellationToken = default)
    {
        var validation = Validate(request.Title, request.StartsAt, request.EndsAt, request.Capacity, request.Fee, request.Chapter);
        if (validation is not null)
        {
            return Result<EventDto>.Failure(validation);
        }

        if (request.CpdUnits is < 0)
        {
            return Result<EventDto>.Failure("CPD units can't be negative.");
        }

        var @event = await db.Events.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (@event is null)
        {
            return Result<EventDto>.NotFound($"Event '{id}' was not found.");
        }

        @event.Title = request.Title.Trim();
        @event.Description = request.Description;
        @event.Chapter = request.Chapter;
        @event.Venue = request.Venue;
        @event.StartsAt = request.StartsAt;
        @event.EndsAt = request.EndsAt;
        @event.Capacity = request.Capacity;
        @event.Fee = request.Fee;
        @event.CpdUnits = request.CpdUnits;
        @event.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var registeredCount = await db.EventRegistrations.CountAsync(
            r => r.EventId == id && r.Status != EventRegistrationStatus.Cancelled, cancellationToken);
        return Result<EventDto>.Success(ToDto(@event, registeredCount));
    }

    private static string? Validate(string title, DateTimeOffset startsAt, DateTimeOffset endsAt, int? capacity, decimal fee, string? chapter)
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
        if (fee < 0)
        {
            return "Fee can't be negative.";
        }
        if (chapter is not null && !Chapters.All.Contains(chapter))
        {
            return $"'{chapter}' is not a recognized chapter.";
        }
        return null;
    }

    private static EventDto ToDto(Event e, int registeredCount) =>
        new(e.Id, e.Title, e.Description, e.Chapter, e.Venue, e.StartsAt, e.EndsAt, e.Capacity,
            registeredCount, e.Fee, e.CpdUnits);
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter EventServiceTests`
Expected: PASS (6 tests). Note: `EF.Functions.ILike` works against Postgres in production but is
**not** supported by the EF InMemory provider used in these unit tests — `GetAllAsync_SearchFiltersByTitle`
will only pass if EF InMemory falls back to client-evaluating the predicate. If that test fails with
a "could not be translated" error, replace the `ILike` call with
`query.Where(e => e.Title.Contains(term, StringComparison.OrdinalIgnoreCase))` instead, which both
providers evaluate the same way, and re-run.

- [ ] **Step 7: Commit**

```bash
git add src/PSMPE.Portal.Application/Events/ tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs
git commit -m "feat: add EventService with event CRUD"
```

---

## 5. Registration, self check-in, evaluation, cancellation

**Files:**
- Create: `src/PSMPE.Portal.Application/Events/Dtos/EventRegistrationDto.cs`
- Modify: `src/PSMPE.Portal.Application/Events/IEventService.cs`
- Modify: `src/PSMPE.Portal.Application/Events/EventService.cs`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs`

- [ ] **Step 1: Create `EventRegistrationDto`**

```csharp
namespace PSMPE.Portal.Application.Events.Dtos;

/// <summary>Status is a string (Status.ToString()), not the raw enum - see the design note at the
/// top of tasks.md: unlike PaymentDto, this is deliberately serialized so the frontend's string
/// literal types actually match what's sent over the wire.</summary>
public record EventRegistrationDto(
    Guid Id,
    Guid EventId,
    string EventTitle,
    DateTimeOffset EventStartsAt,
    Guid MemberId,
    string MemberName,
    string? MembershipNo,
    string Status,
    DateTimeOffset? AttendedAt,
    bool? IsSelfCheckIn,
    int? EvaluationRating,
    string? EvaluationComments,
    DateTimeOffset? EvaluationSubmittedAt,
    int? CreditUnits,
    Guid? PaymentId,
    string? PaymentStatus,
    string? PaymentRejectedReason);
```

- [ ] **Step 2: Add the new members to `IEventService`**

```csharp
    Task<Result<EventRegistrationDto>> RegisterAsync(Guid userId, Guid eventId, CancellationToken cancellationToken = default);

    Task<Result> CancelRegistrationAsync(Guid userId, Guid registrationId, CancellationToken cancellationToken = default);

    Task<Result> CheckInAsync(Guid userId, Guid registrationId, CancellationToken cancellationToken = default);

    Task<Result> SetAttendanceAsync(Guid registrationId, bool attended, Guid adminUserId, CancellationToken cancellationToken = default);

    Task<Result> SubmitEvaluationAsync(Guid userId, Guid registrationId, int rating, string? comments, CancellationToken cancellationToken = default);
```

- [ ] **Step 3: Write the failing tests**

Append to `EventServiceTests.cs` (reuses `ValidCreateRequest` from Task 4):

```csharp
    private static async Task<Member> SeedMemberAsync(TestDbContext db, string email = "m@example.com")
    {
        var user = new ApplicationUser { UserName = email, Email = email };
        db.Add(user);
        var member = new Member { UserId = user.Id, User = user, FirstName = "Juan", LastName = "Dela Cruz", Chapter = "NCR" };
        db.Members.Add(member);
        await db.SaveChangesAsync();
        return member;
    }

    [Fact]
    public async Task RegisterAsync_CreatesRegistrationInRegisteredStatus()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberAsync(db);

        var result = await service.RegisterAsync(member.UserId, @event.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("Registered", result.Value!.Status);
    }

    [Fact]
    public async Task RegisterAsync_Twice_SecondCallFails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberAsync(db);
        await service.RegisterAsync(member.UserId, @event.Id);

        var result = await service.RegisterAsync(member.UserId, @event.Id);

        Assert.False(result.Succeeded);
    }

    /// <summary>A cancelled registration frees the member to register again - this is what makes
    /// Cancelled a real off-ramp rather than a dead end.</summary>
    [Fact]
    public async Task RegisterAsync_AfterCancelling_Succeeds()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberAsync(db);
        var first = await service.RegisterAsync(member.UserId, @event.Id);
        await service.CancelRegistrationAsync(member.UserId, first.Value!.Id);

        var result = await service.RegisterAsync(member.UserId, @event.Id);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task CheckInAsync_BeforePaymentVerified_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id)).Value!;

        var result = await service.CheckInAsync(member.UserId, registration.Id);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task CheckInAsync_BeforeEventStarts_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!; // starts in 10 days
        var member = await SeedMemberAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id)).Value!;
        await MarkPaymentVerifiedAsync(db, registration.Id);

        var result = await service.CheckInAsync(member.UserId, registration.Id);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task CheckInAsync_AfterEventStarts_Succeeds_AsSelfCheckIn()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var pastEvent = new Event
        {
            Title = "Past Seminar", StartsAt = DateTimeOffset.UtcNow.AddHours(-1),
            EndsAt = DateTimeOffset.UtcNow.AddHours(3), Fee = 0m,
        };
        db.Events.Add(pastEvent);
        await db.SaveChangesAsync();
        var member = await SeedMemberAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, pastEvent.Id)).Value!;
        await MarkPaymentVerifiedAsync(db, registration.Id);

        var result = await service.CheckInAsync(member.UserId, registration.Id);

        Assert.True(result.Succeeded);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.Attended, updated!.Status);
        Assert.Null(updated.AttendedByUserId);
    }

    [Fact]
    public async Task SetAttendanceAsync_Admin_RecordsAdminAsActor()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id)).Value!;
        await MarkPaymentVerifiedAsync(db, registration.Id);
        var adminUserId = Guid.NewGuid();

        var result = await service.SetAttendanceAsync(registration.Id, attended: true, adminUserId);

        Assert.True(result.Succeeded);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.Attended, updated!.Status);
        Assert.Equal(adminUserId, updated.AttendedByUserId);
    }

    [Fact]
    public async Task SubmitEvaluationAsync_BeforeAttended_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id)).Value!;

        var result = await service.SubmitEvaluationAsync(member.UserId, registration.Id, rating: 5, comments: "Great");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SubmitEvaluationAsync_AfterAttended_MovesToEvaluationSubmitted()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id)).Value!;
        await MarkAttendedAsync(db, registration.Id);

        var result = await service.SubmitEvaluationAsync(member.UserId, registration.Id, rating: 4, comments: "Good session");

        Assert.True(result.Succeeded);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.EvaluationSubmitted, updated!.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task SubmitEvaluationAsync_RatingOutOfRange_Fails(int rating)
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id)).Value!;
        await MarkAttendedAsync(db, registration.Id);

        var result = await service.SubmitEvaluationAsync(member.UserId, registration.Id, rating, comments: null);

        Assert.False(result.Succeeded);
    }

    private static async Task MarkPaymentVerifiedAsync(TestDbContext db, Guid registrationId)
    {
        var registration = await db.EventRegistrations.FindAsync(registrationId);
        registration!.Status = EventRegistrationStatus.PaymentVerified;
        await db.SaveChangesAsync();
    }

    private static async Task MarkAttendedAsync(TestDbContext db, Guid registrationId)
    {
        var registration = await db.EventRegistrations.FindAsync(registrationId);
        registration!.Status = EventRegistrationStatus.Attended;
        registration.AttendedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter EventServiceTests`
Expected: FAIL to compile — the new `IEventService` members have no implementation yet.

- [ ] **Step 5: Implement the new `EventService` members**

Add to `EventService.cs`:

```csharp
    public async Task<Result<EventRegistrationDto>> RegisterAsync(Guid userId, Guid eventId, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (member is null)
        {
            return Result<EventRegistrationDto>.Failure("No member profile found for this account.");
        }

        var @event = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
        if (@event is null)
        {
            return Result<EventRegistrationDto>.NotFound($"Event '{eventId}' was not found.");
        }

        var alreadyRegistered = await db.EventRegistrations.AnyAsync(
            r => r.EventId == eventId && r.MemberId == member.Id && r.Status != EventRegistrationStatus.Cancelled,
            cancellationToken);
        if (alreadyRegistered)
        {
            return Result<EventRegistrationDto>.Conflict("You're already registered for this event.");
        }

        var registration = new EventRegistration { EventId = eventId, MemberId = member.Id };
        db.EventRegistrations.Add(registration);
        await db.SaveChangesAsync(cancellationToken);

        return Result<EventRegistrationDto>.Success(ToDto(registration, @event, member, payment: null));
    }

    public async Task<Result> CancelRegistrationAsync(Guid userId, Guid registrationId, CancellationToken cancellationToken = default)
    {
        var registration = await db.EventRegistrations
            .Include(r => r.Member)
            .FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken);
        if (registration is null)
        {
            return Result.NotFound($"Registration '{registrationId}' was not found.");
        }
        if (registration.Member.UserId != userId)
        {
            return Result.Forbidden("This isn't your registration.");
        }

        // Once a payment is verified, cancelling would need refund handling - explicitly out of
        // scope (see proposal.md's "Not Built"). Before that point there's nothing to unwind.
        if (registration.Status is not (EventRegistrationStatus.Registered or EventRegistrationStatus.PaymentSubmitted or EventRegistrationStatus.Rejected))
        {
            return Result.Failure("This registration can no longer be cancelled.");
        }

        registration.Status = EventRegistrationStatus.Cancelled;
        registration.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> CheckInAsync(Guid userId, Guid registrationId, CancellationToken cancellationToken = default)
    {
        var registration = await db.EventRegistrations
            .Include(r => r.Member)
            .Include(r => r.Event)
            .FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken);
        if (registration is null)
        {
            return Result.NotFound($"Registration '{registrationId}' was not found.");
        }
        if (registration.Member.UserId != userId)
        {
            return Result.Forbidden("This isn't your registration.");
        }
        if (registration.Status != EventRegistrationStatus.PaymentVerified)
        {
            return Result.Failure("You need a verified payment before you can check in.");
        }
        if (DateTimeOffset.UtcNow < registration.Event.StartsAt)
        {
            return Result.Failure("This event hasn't started yet.");
        }

        registration.Status = EventRegistrationStatus.Attended;
        registration.AttendedAt = DateTimeOffset.UtcNow;
        registration.AttendedByUserId = null; // self check-in
        registration.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> SetAttendanceAsync(Guid registrationId, bool attended, Guid adminUserId, CancellationToken cancellationToken = default)
    {
        var registration = await db.EventRegistrations.FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken);
        if (registration is null)
        {
            return Result.NotFound($"Registration '{registrationId}' was not found.");
        }

        if (attended)
        {
            registration.Status = EventRegistrationStatus.Attended;
            registration.AttendedAt = DateTimeOffset.UtcNow;
            registration.AttendedByUserId = adminUserId;
        }
        else
        {
            // Correcting a mistaken check-in - only valid before an evaluation exists, otherwise
            // completion would outlive the attendance it depended on.
            if (registration.Status == EventRegistrationStatus.EvaluationSubmitted)
            {
                return Result.Failure("This registration already has a submitted evaluation - it can't be un-attended.");
            }
            registration.Status = EventRegistrationStatus.PaymentVerified;
            registration.AttendedAt = null;
            registration.AttendedByUserId = null;
        }
        registration.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> SubmitEvaluationAsync(Guid userId, Guid registrationId, int rating, string? comments, CancellationToken cancellationToken = default)
    {
        if (rating is < 1 or > 5)
        {
            return Result.Failure("Rating must be between 1 and 5.");
        }

        var registration = await db.EventRegistrations
            .Include(r => r.Member)
            .FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken);
        if (registration is null)
        {
            return Result.NotFound($"Registration '{registrationId}' was not found.");
        }
        if (registration.Member.UserId != userId)
        {
            return Result.Forbidden("This isn't your registration.");
        }
        if (registration.Status != EventRegistrationStatus.Attended)
        {
            return Result.Failure("You need to be marked attended before you can submit the evaluation.");
        }

        registration.Status = EventRegistrationStatus.EvaluationSubmitted;
        registration.EvaluationRating = rating;
        registration.EvaluationComments = comments;
        registration.EvaluationSubmittedAt = DateTimeOffset.UtcNow;
        registration.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
```

Add the DTO-mapping helper (used here and reused by Tasks 6–7):

```csharp
    private static EventRegistrationDto ToDto(EventRegistration r, Event e, Member m, Payment? payment) =>
        new(
            r.Id, r.EventId, e.Title, e.StartsAt, r.MemberId, $"{m.FirstName} {m.LastName}", m.MembershipNo,
            r.Status.ToString(), r.AttendedAt,
            r.AttendedAt is null ? null : r.AttendedByUserId is null,
            r.EvaluationRating, r.EvaluationComments, r.EvaluationSubmittedAt,
            CpdCredit.For(r, e),
            payment?.Id, payment?.Status.ToString(), payment?.RejectedReason);
```

This references `CpdCredit.For`, which doesn't exist yet — that's Task 6, Step 1. The build won't
succeed until then; that's expected for this step.

- [ ] **Step 6: Create the `CpdCredit` helper now, out of order, so Task 5 compiles**

```csharp
namespace PSMPE.Portal.Application.Events;

using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

/// <summary>
/// CPD credit is computed here, never stored on EventRegistration - see the design note at the top
/// of tasks.md and add-events-cpd-tracker/proposal.md. A registration only counts once it has
/// completed the full loop (evaluation submitted) AND the event's unit count has been set.
/// </summary>
internal static class CpdCredit
{
    public static int? For(EventRegistration registration, Event @event) =>
        registration.Status == EventRegistrationStatus.EvaluationSubmitted && @event.CpdUnits.HasValue
            ? @event.CpdUnits
            : null;
}
```

Create this at `src/PSMPE.Portal.Application/Events/CpdCredit.cs` (Task 6 will add its own unit
tests for this class; this step only unblocks the build).

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter EventServiceTests`
Expected: PASS (all tests from Steps 3 and Task 4).

- [ ] **Step 8: Commit**

```bash
git add src/PSMPE.Portal.Application/Events/ tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs
git commit -m "feat: add registration, check-in, evaluation and cancellation to EventService"
```

---

## 6. CPD credit computation and "My CPD" query

**Files:**
- Create: `src/PSMPE.Portal.Application/Events/Dtos/MyCpdSummaryDto.cs`
- Modify: `src/PSMPE.Portal.Application/Events/IEventService.cs`
- Modify: `src/PSMPE.Portal.Application/Events/EventService.cs`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Events/CpdCreditTests.cs`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs`

- [ ] **Step 1: Write the failing tests for `CpdCredit`**

```csharp
using PSMPE.Portal.Application.Events;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Events;

public class CpdCreditTests
{
    private static Event EventWithUnits(int? units) => new() { Title = "X", CpdUnits = units };

    [Fact]
    public void For_EvaluationSubmitted_AndUnitsSet_ReturnsUnits()
    {
        var registration = new EventRegistration { Status = EventRegistrationStatus.EvaluationSubmitted };

        var credit = CpdCredit.For(registration, EventWithUnits(4));

        Assert.Equal(4, credit);
    }

    [Fact]
    public void For_EvaluationSubmitted_ButUnitsStillTbd_ReturnsNull()
    {
        var registration = new EventRegistration { Status = EventRegistrationStatus.EvaluationSubmitted };

        var credit = CpdCredit.For(registration, EventWithUnits(null));

        Assert.Null(credit);
    }

    [Fact]
    public void For_AttendedButNoEvaluation_ReturnsNull_EvenWithUnitsSet()
    {
        var registration = new EventRegistration { Status = EventRegistrationStatus.Attended };

        var credit = CpdCredit.For(registration, EventWithUnits(4));

        Assert.Null(credit);
    }
}
```

Note: `CpdCredit` and its implementation already exist from Task 5 Step 6 (added early so that task
could compile) — this step's tests should already pass. Run them anyway to confirm:

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter CpdCreditTests`
Expected: PASS (3 tests).

- [ ] **Step 2: Create `MyCpdSummaryDto`**

```csharp
namespace PSMPE.Portal.Application.Events.Dtos;

public record MyCpdSummaryDto(int TotalUnits, IReadOnlyList<EventRegistrationDto> Registrations);
```

- [ ] **Step 3: Add `GetMyCpdAsync` to `IEventService`**

```csharp
    Task<MyCpdSummaryDto> GetMyCpdAsync(Guid userId, CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Write the failing test**

Append to `EventServiceTests.cs`:

```csharp
    [Fact]
    public async Task GetMyCpdAsync_SumsOnlyEvaluationSubmittedRegistrationsWithUnitsSet()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var member = await SeedMemberAsync(db);

        var completedWithUnits = (await service.CreateAsync(ValidCreateRequest("Completed, units set"))).Value!;
        await service.UpdateAsync(completedWithUnits.Id, ToUpdateRequest(completedWithUnits) with { CpdUnits = 4 });
        var reg1 = (await service.RegisterAsync(member.UserId, completedWithUnits.Id)).Value!;
        await MarkAttendedAsync(db, reg1.Id);
        await service.SubmitEvaluationAsync(member.UserId, reg1.Id, 5, null);

        var completedNoUnits = (await service.CreateAsync(ValidCreateRequest("Completed, units TBD"))).Value!;
        var reg2 = (await service.RegisterAsync(member.UserId, completedNoUnits.Id)).Value!;
        await MarkAttendedAsync(db, reg2.Id);
        await service.SubmitEvaluationAsync(member.UserId, reg2.Id, 5, null);

        var notCompleted = (await service.CreateAsync(ValidCreateRequest("Not completed"))).Value!;
        await service.UpdateAsync(notCompleted.Id, ToUpdateRequest(notCompleted) with { CpdUnits = 10 });
        await service.RegisterAsync(member.UserId, notCompleted.Id);

        var summary = await service.GetMyCpdAsync(member.UserId);

        Assert.Equal(4, summary.TotalUnits);
        Assert.Equal(3, summary.Registrations.Count);
    }

    private static UpdateEventRequest ToUpdateRequest(EventDto e) =>
        new(e.Title, e.Description, e.Chapter, e.Venue, e.StartsAt, e.EndsAt, e.Capacity, e.Fee, e.CpdUnits);
```

- [ ] **Step 5: Run the test to verify it fails**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter GetMyCpdAsync_SumsOnlyEvaluationSubmittedRegistrationsWithUnitsSet`
Expected: FAIL to compile — `GetMyCpdAsync` has no implementation.

- [ ] **Step 6: Implement `GetMyCpdAsync`**

Add to `EventService.cs`:

```csharp
    public async Task<MyCpdSummaryDto> GetMyCpdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (member is null)
        {
            return new MyCpdSummaryDto(0, []);
        }

        var registrations = await db.EventRegistrations
            .Include(r => r.Event)
            .Where(r => r.MemberId == member.Id && r.Status != EventRegistrationStatus.Cancelled)
            .OrderByDescending(r => r.Event.StartsAt)
            .ToListAsync(cancellationToken);

        var registrationIds = registrations.Select(r => r.Id).ToList();
        var payments = await db.Payments
            .Where(p => p.EventRegistrationId != null && registrationIds.Contains(p.EventRegistrationId!.Value))
            .ToDictionaryAsync(p => p.EventRegistrationId!.Value, cancellationToken);

        var dtos = registrations
            .Select(r => ToDto(r, r.Event, member, payments.GetValueOrDefault(r.Id)))
            .ToList();

        var total = dtos.Sum(d => d.CreditUnits ?? 0);
        return new MyCpdSummaryDto(total, dtos);
    }
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter EventServiceTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/PSMPE.Portal.Application/Events/ tests/PSMPE.Portal.Application.UnitTests/Events/
git commit -m "feat: add GetMyCpdAsync and CpdCredit tests"
```

---

## 7. Roster query

**Files:**
- Modify: `src/PSMPE.Portal.Application/Events/IEventService.cs`
- Modify: `src/PSMPE.Portal.Application/Events/EventService.cs`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs`

- [ ] **Step 1: Add `GetRosterAsync` to `IEventService`**

```csharp
    Task<Result<IReadOnlyList<EventRegistrationDto>>> GetRosterAsync(Guid eventId, CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Write the failing test**

Append to `EventServiceTests.cs`:

```csharp
    [Fact]
    public async Task GetRosterAsync_ReturnsAllNonCancelledRegistrationsForTheEvent()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var memberA = await SeedMemberAsync(db, "a@example.com");
        var memberB = await SeedMemberAsync(db, "b@example.com");
        var regA = (await service.RegisterAsync(memberA.UserId, @event.Id)).Value!;
        await service.RegisterAsync(memberB.UserId, @event.Id);
        await service.CancelRegistrationAsync(memberA.UserId, regA.Id);
        await service.RegisterAsync(memberA.UserId, @event.Id); // re-registers after cancelling

        var result = await service.GetRosterAsync(@event.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Count);
    }

    [Fact]
    public async Task GetRosterAsync_UnknownEvent_ReturnsNotFound()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);

        var result = await service.GetRosterAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter GetRosterAsync`
Expected: FAIL to compile.

- [ ] **Step 4: Implement `GetRosterAsync`**

```csharp
    public async Task<Result<IReadOnlyList<EventRegistrationDto>>> GetRosterAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var @event = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
        if (@event is null)
        {
            return Result<IReadOnlyList<EventRegistrationDto>>.NotFound($"Event '{eventId}' was not found.");
        }

        var registrations = await db.EventRegistrations
            .Include(r => r.Member)
            .Where(r => r.EventId == eventId && r.Status != EventRegistrationStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var registrationIds = registrations.Select(r => r.Id).ToList();
        var payments = await db.Payments
            .Where(p => p.EventRegistrationId != null && registrationIds.Contains(p.EventRegistrationId!.Value))
            .ToDictionaryAsync(p => p.EventRegistrationId!.Value, cancellationToken);

        IReadOnlyList<EventRegistrationDto> roster = registrations
            .Select(r => ToDto(r, @event, r.Member, payments.GetValueOrDefault(r.Id)))
            .ToList();
        return Result<IReadOnlyList<EventRegistrationDto>>.Success(roster);
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter EventServiceTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/PSMPE.Portal.Application/Events/ tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs
git commit -m "feat: add GetRosterAsync to EventService"
```

---

## 8. Payment integration

**Files:**
- Modify: `src/PSMPE.Portal.Application/Payments/IPaymentService.cs`
- Modify: `src/PSMPE.Portal.Application/Payments/PaymentService.cs`
- Modify: `src/PSMPE.Portal.Application/Payments/PaymentVerification.cs`
- Modify: `src/PSMPE.Portal.Application/Members/MemberService.cs:542`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Payments/PaymentServiceTests.cs`

This is the one place existing behavior changes — read `PaymentVerification.Apply` and
`PaymentService.VerifyAsync`/`RejectAsync` in full before editing, so the `NewMembership`/`Renewal`
paths are untouched. **`PaymentVerification.Apply`'s signature changes** (a third parameter is
inserted), and it has a second existing caller besides `PaymentService.VerifyAsync`:
`MemberService.ApproveAsync` at line 542 calls `PaymentVerification.Apply(paymentResult.Value!,
member, decidedByUserId);` directly, when it admits an application and accepts its registration
payment in one transaction. That call site must be updated in the same commit as the signature
change (Step 6 below) or the solution won't build.

- [ ] **Step 1: Add `SubmitForEventRegistrationAsync` to `IPaymentService`**

```csharp
    /// <summary>Creates the Payment for one EventRegistration. Separate from SubmitAsync (which
    /// derives Kind from the member's own membership state) because an event payment is scoped to
    /// a specific registration the caller must own.</summary>
    Task<Result<PaymentDto>> SubmitForEventRegistrationAsync(
        Guid userId, Guid eventRegistrationId, SubmitPaymentRequest request, CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Write the failing tests**

Append to `PaymentServiceTests.cs`:

```csharp
using PSMPE.Portal.Domain.Enums;

    private static async Task<(Member Member, PSMPE.Portal.Domain.Entities.Event Event, PSMPE.Portal.Domain.Entities.EventRegistration Registration)>
        SeedEventRegistrationAsync(TestDbContext db, EventRegistrationStatus status = EventRegistrationStatus.Registered)
    {
        var user = new ApplicationUser { UserName = $"{Guid.NewGuid()}@example.com", Email = $"{Guid.NewGuid()}@example.com" };
        db.Add(user);
        var member = new Member { UserId = user.Id, User = user, FirstName = "Juan", LastName = "Dela Cruz", Chapter = "NCR" };
        db.Members.Add(member);
        var @event = new PSMPE.Portal.Domain.Entities.Event { Title = "Seminar", StartsAt = DateTimeOffset.UtcNow.AddDays(5), EndsAt = DateTimeOffset.UtcNow.AddDays(5).AddHours(3), Fee = 500m };
        db.Events.Add(@event);
        await db.SaveChangesAsync();
        var registration = new PSMPE.Portal.Domain.Entities.EventRegistration { EventId = @event.Id, MemberId = member.Id, Status = status };
        db.EventRegistrations.Add(registration);
        await db.SaveChangesAsync();
        return (member, @event, registration);
    }

    [Fact]
    public async Task SubmitForEventRegistrationAsync_CreatesPaymentAndAdvancesRegistration()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (member, _, registration) = await SeedEventRegistrationAsync(db);

        var result = await service.SubmitForEventRegistrationAsync(
            member.UserId, registration.Id, new SubmitPaymentRequest(500m, "REF-1", DateOnly.FromDateTime(DateTime.UtcNow)));

        Assert.True(result.Succeeded);
        Assert.Equal(PaymentKind.EventRegistration, result.Value!.Kind);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.PaymentSubmitted, updated!.Status);
    }

    [Fact]
    public async Task SubmitForEventRegistrationAsync_NotOwner_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (_, _, registration) = await SeedEventRegistrationAsync(db);
        var otherUserId = Guid.NewGuid();

        var result = await service.SubmitForEventRegistrationAsync(
            otherUserId, registration.Id, new SubmitPaymentRequest(500m, null, DateOnly.FromDateTime(DateTime.UtcNow)));

        Assert.False(result.Succeeded);
    }

    /// <summary>Verifying an event payment must NOT require Member.ApprovedAt - unlike membership
    /// dues, event registration has nothing to do with application approval.</summary>
    [Fact]
    public async Task VerifyAsync_EventRegistration_DoesNotRequireMemberApproval()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (member, _, registration) = await SeedEventRegistrationAsync(db);
        Assert.Null(member.ApprovedAt); // unapproved on purpose
        var submitted = await service.SubmitForEventRegistrationAsync(
            member.UserId, registration.Id, new SubmitPaymentRequest(500m, null, DateOnly.FromDateTime(DateTime.UtcNow)));
        var payment = await db.Payments.FindAsync(submitted.Value!.Id);
        payment!.ProofStorageKey = "proof/key.jpg";
        await db.SaveChangesAsync();

        var result = await service.VerifyAsync(payment.Id, Guid.NewGuid());

        Assert.True(result.Succeeded);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.PaymentVerified, updated!.Status);
    }

    [Fact]
    public async Task RejectAsync_EventRegistration_SetsRegistrationRejected()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (member, _, registration) = await SeedEventRegistrationAsync(db);
        var submitted = await service.SubmitForEventRegistrationAsync(
            member.UserId, registration.Id, new SubmitPaymentRequest(500m, null, DateOnly.FromDateTime(DateTime.UtcNow)));
        var payment = await db.Payments.FindAsync(submitted.Value!.Id);
        payment!.ProofStorageKey = "proof/key.jpg";
        await db.SaveChangesAsync();

        var result = await service.RejectAsync(payment.Id, "Illegible proof", Guid.NewGuid());

        Assert.True(result.Succeeded);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.Rejected, updated!.Status);
    }

    /// <summary>The scenario RejectAsync's test above sets up but doesn't finish: after a
    /// rejection, the member must be able to submit a fresh payment against the *same*
    /// registration rather than being stuck, per specs/events/spec.md.</summary>
    [Fact]
    public async Task SubmitForEventRegistrationAsync_AfterRejection_CreatesNewSubmission()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (member, _, registration) = await SeedEventRegistrationAsync(db);
        var first = await service.SubmitForEventRegistrationAsync(
            member.UserId, registration.Id, new SubmitPaymentRequest(500m, null, DateOnly.FromDateTime(DateTime.UtcNow)));
        var firstPayment = await db.Payments.FindAsync(first.Value!.Id);
        firstPayment!.ProofStorageKey = "proof/key.jpg";
        await db.SaveChangesAsync();
        await service.RejectAsync(firstPayment.Id, "Illegible", Guid.NewGuid());

        var result = await service.SubmitForEventRegistrationAsync(
            member.UserId, registration.Id, new SubmitPaymentRequest(500m, "REF-2", DateOnly.FromDateTime(DateTime.UtcNow)));

        Assert.True(result.Succeeded);
        Assert.NotEqual(firstPayment.Id, result.Value!.Id);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.PaymentSubmitted, updated!.Status);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter Payments`
Expected: FAIL to compile — `SubmitForEventRegistrationAsync` doesn't exist, and `VerifyAsync`
still requires `Member.ApprovedAt` unconditionally.

- [ ] **Step 4: Implement `SubmitForEventRegistrationAsync`**

Add to `PaymentService.cs`:

```csharp
    public async Task<Result<PaymentDto>> SubmitForEventRegistrationAsync(
        Guid userId, Guid eventRegistrationId, SubmitPaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
        {
            return Result<PaymentDto>.Failure("Enter the amount paid.");
        }

        var registration = await db.EventRegistrations
            .Include(r => r.Member)
            .FirstOrDefaultAsync(r => r.Id == eventRegistrationId, cancellationToken);
        if (registration is null)
        {
            return Result<PaymentDto>.NotFound($"Registration '{eventRegistrationId}' was not found.");
        }
        if (registration.Member.UserId != userId)
        {
            return Result<PaymentDto>.Forbidden("This isn't your registration.");
        }
        // Registered (first attempt) or Rejected (resubmitting after a rejection) are both valid -
        // see specs/events/spec.md's "A rejected event payment can be resubmitted" scenario. Any
        // other status means a payment is already pending or already verified.
        if (registration.Status is not (EventRegistrationStatus.Registered or EventRegistrationStatus.Rejected))
        {
            return Result<PaymentDto>.Conflict("A payment already exists for this registration.");
        }

        var payment = new Payment
        {
            MemberId = registration.MemberId,
            EventRegistrationId = registration.Id,
            Kind = PaymentKind.EventRegistration,
            Amount = request.Amount,
            ReferenceNo = request.ReferenceNo,
            PaidOn = request.PaidOn,
            Status = PaymentStatus.Submitted,
        };
        db.Payments.Add(payment);
        registration.Status = EventRegistrationStatus.PaymentSubmitted;
        registration.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Result<PaymentDto>.Success(ToDto(payment, registration.Member));
    }
```

If `PaymentService` doesn't already have a private `ToDto(Payment, Member)` mapping helper matching
this signature, adapt the call to whatever mapping method `SubmitAsync` already uses instead of
introducing a duplicate — check `PaymentService.cs` for the existing helper before adding a new one.

- [ ] **Step 5: Extend `PaymentVerification.Apply` for the new kind**

Replace the whole file:

```csharp
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Payments;

/// <summary>
/// The effect of accepting a payment, in one place - now branching by Kind, since EventRegistration
/// payments flip a different aggregate (EventRegistration.Status) than membership payments
/// (Member.Status/RenewalDueDate). See add-events-cpd-tracker/proposal.md.
/// </summary>
internal static class PaymentVerification
{
    /// <summary>Caller must have already established the payment is Submitted and has proof.
    /// Exactly one of <paramref name="member"/>/<paramref name="eventRegistration"/> is non-null,
    /// matching payment.Kind.</summary>
    public static void Apply(Payment payment, Member? member, EventRegistration? eventRegistration, Guid decidedByUserId)
    {
        if (payment.Kind == PaymentKind.EventRegistration)
        {
            ApplyEventRegistration(payment, eventRegistration!, decidedByUserId);
            return;
        }

        ApplyMembership(payment, member!, decidedByUserId);
    }

    private static void ApplyMembership(Payment payment, Member member, Guid decidedByUserId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        member.RenewalDueDate = payment.Kind switch
        {
            PaymentKind.NewMembership => DateOnly.FromDateTime(member.ApprovedAt!.Value.UtcDateTime).AddYears(1),
            _ => (member.RenewalDueDate ?? today).AddYears(1),
        };

        member.Status = MembershipStatus.Active;
        member.UpdatedAt = DateTimeOffset.UtcNow;

        payment.Status = PaymentStatus.Verified;
        payment.RejectedReason = null;
        payment.DecidedByUserId = decidedByUserId;
        payment.DecidedAt = DateTimeOffset.UtcNow;
        payment.CoversUntil = member.RenewalDueDate;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ApplyEventRegistration(Payment payment, EventRegistration registration, Guid decidedByUserId)
    {
        registration.Status = EventRegistrationStatus.PaymentVerified;
        registration.UpdatedAt = DateTimeOffset.UtcNow;

        payment.Status = PaymentStatus.Verified;
        payment.RejectedReason = null;
        payment.DecidedByUserId = decidedByUserId;
        payment.DecidedAt = DateTimeOffset.UtcNow;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
```

- [ ] **Step 6: Fix `MemberService.ApproveAsync`'s call site for the new `Apply` signature**

In `src/PSMPE.Portal.Application/Members/MemberService.cs`, line 542 currently reads:

```csharp
        PaymentVerification.Apply(paymentResult.Value!, member, decidedByUserId);
```

Change it to:

```csharp
        PaymentVerification.Apply(paymentResult.Value!, member, eventRegistration: null, decidedByUserId);
```

This is the only other caller of `PaymentVerification.Apply` in the codebase — approving a
membership application never involves an `EventRegistration`, so this is always `null` here.

- [ ] **Step 7: Update `PaymentService.VerifyAsync` to branch by kind**

Replace the existing `VerifyAsync` method with:

```csharp
    public async Task<Result> VerifyAsync(Guid paymentId, Guid decidedByUserId, CancellationToken cancellationToken = default)
    {
        var payment = await db.Payments
            .Include(p => p.Member)
            .Include(p => p.EventRegistration)
            .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null)
        {
            return Result.NotFound($"Payment '{paymentId}' was not found.");
        }

        if (payment.Status == PaymentStatus.Verified)
        {
            return Result.Success();
        }

        if (payment.Status == PaymentStatus.Rejected)
        {
            return Result.Failure("This payment was rejected. The member needs to submit a new one.");
        }

        if (payment.ProofStorageKey is null)
        {
            return Result.Failure("This payment has no proof attached - there's nothing to verify against.");
        }

        if (payment.Kind == PaymentKind.EventRegistration)
        {
            if (payment.EventRegistration is null)
            {
                return Result.Failure("This payment isn't linked to an event registration.");
            }

            PaymentVerification.Apply(payment, member: null, payment.EventRegistration, decidedByUserId);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        var member = payment.Member;
        if (member.ApprovedAt is null)
        {
            return Result.Failure("This member's application hasn't been approved yet, so their payment can't activate a membership.");
        }

        PaymentVerification.Apply(payment, member, eventRegistration: null, decidedByUserId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
```

- [ ] **Step 8: Update `PaymentService.RejectAsync` to also flip the registration**

Find `RejectAsync` in `PaymentService.cs`. It currently sets `payment.Status = PaymentStatus.Rejected`
plus the reason/decider fields and does **not** touch `Member`. Add an `EventRegistration` include
to its query (same `.Include(p => p.EventRegistration)` as `VerifyAsync`), and immediately after the
line that sets `payment.Status = PaymentStatus.Rejected;`, add:

```csharp
        if (payment.Kind == PaymentKind.EventRegistration && payment.EventRegistration is not null)
        {
            payment.EventRegistration.Status = EventRegistrationStatus.Rejected;
            payment.EventRegistration.UpdatedAt = DateTimeOffset.UtcNow;
        }
```

Membership payments (`NewMembership`/`Renewal`) are unaffected — `payment.EventRegistration` is
always null for those, so the `if` never triggers.

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter Payments`
Expected: PASS — every existing `PaymentServiceTests` case (NewMembership/Renewal) still passes
unchanged, plus the five new ones from Step 2 (four Payment-side, one Member-approval-flow check
implicitly covered by the existing `MemberServiceTests` for `ApproveAsync`, which now exercises
the updated `Apply` call site).

- [ ] **Step 10: Full solution build and test run**

Run: `dotnet build src/PSMPE.Portal.sln && dotnet test src/PSMPE.Portal.sln`
Expected: build succeeds, all tests pass — this is the first point where every backend layer touches
the new feature, so it's worth the full-solution check rather than a filtered one. This is also the
step that would catch a missed `PaymentVerification.Apply` call site, since `MemberServiceTests`
covering `ApproveAsync` runs here even though it isn't filtered by `--filter Payments` above.

- [ ] **Step 11: Commit**

```bash
git add src/PSMPE.Portal.Application/Payments/ src/PSMPE.Portal.Application/Members/MemberService.cs \
  tests/PSMPE.Portal.Application.UnitTests/Payments/PaymentServiceTests.cs
git commit -m "feat: wire event registration payments into PaymentService"
```

---

## 9. Certificate PDF generation

**Files:**
- Create: `src/PSMPE.Portal.Application/Events/ICertificateGenerator.cs`
- Create: `src/PSMPE.Portal.Infrastructure/Services/EventCertificateGenerator.cs`
- Modify: `src/PSMPE.Portal.WebAPI/Program.cs`
- Modify: `src/PSMPE.Portal.Infrastructure/PSMPE.Portal.Infrastructure.csproj` (via `dotnet add package`)

- [ ] **Step 1: Add the QuestPDF package**

```bash
dotnet add src/PSMPE.Portal.Infrastructure/PSMPE.Portal.Infrastructure.csproj package QuestPDF
```

Expected: `PSMPE.Portal.Infrastructure.csproj` gains a `<PackageReference Include="QuestPDF" ... />`
line.

- [ ] **Step 2: Declare the Community license at startup**

QuestPDF requires an explicit license declaration before generating any document. In
`src/PSMPE.Portal.WebAPI/Program.cs`, add near the top (after the `using` directives, before
`var builder = WebApplication.CreateBuilder(args);`):

```csharp
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
```

- [ ] **Step 3: Define `ICertificateGenerator`**

```csharp
namespace PSMPE.Portal.Application.Events;

public record CertificateData(string MemberName, string EventTitle, DateTimeOffset EventDate, int CpdUnits, Guid CertificateId);

/// <summary>Generates the CPD credit certificate PDF, on demand, from data the caller has already
/// confirmed is eligible (EvaluationSubmitted + CpdUnits set) - this interface has no opinion on
/// eligibility, only rendering.</summary>
public interface ICertificateGenerator
{
    byte[] Generate(CertificateData data);
}
```

- [ ] **Step 4: Implement `EventCertificateGenerator` with QuestPDF**

```csharp
using PSMPE.Portal.Application.Events;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PSMPE.Portal.Infrastructure.Services;

public class EventCertificateGenerator : ICertificateGenerator
{
    public byte[] Generate(CertificateData data)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(14));

                page.Content().Column(column =>
                {
                    column.Spacing(16);
                    column.Item().AlignCenter().Text("Certificate of CPD Credit")
                        .FontSize(28).Bold();
                    column.Item().AlignCenter().Text("Philippine Society of Master Plumbing Engineers")
                        .FontSize(14);
                    column.Item().PaddingTop(20).AlignCenter().Text("This certifies that").FontSize(14);
                    column.Item().AlignCenter().Text(data.MemberName).FontSize(22).Bold();
                    column.Item().AlignCenter().Text("has completed").FontSize(14);
                    column.Item().AlignCenter().Text(data.EventTitle).FontSize(18).Bold();
                    column.Item().AlignCenter().Text(data.EventDate.ToString("MMMM d, yyyy")).FontSize(14);
                    column.Item().PaddingTop(20).AlignCenter()
                        .Text($"and is awarded {data.CpdUnits} CPD unit(s)").FontSize(16).Bold();
                    column.Item().PaddingTop(30).AlignCenter()
                        .Text($"Certificate ID: {data.CertificateId}").FontSize(10);
                });
            });
        });

        return document.GeneratePdf();
    }
}
```

- [ ] **Step 5: Register the generator in DI**

In `src/PSMPE.Portal.Infrastructure/DependencyInjection.cs`, find the existing
`services.AddScoped<IPaymentService, PaymentService>();`-style registration block and add directly
below it:

```csharp
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<ICertificateGenerator, EventCertificateGenerator>();
```

(Add the corresponding `using PSMPE.Portal.Application.Events;` and
`using PSMPE.Portal.Infrastructure.Services;` if not already present in that file.)

- [ ] **Step 6: Build to confirm it compiles**

Run: `dotnet build src/PSMPE.Portal.sln`
Expected: build succeeds (0 errors).

- [ ] **Step 7: Manual smoke test**

There's no automated test for visual PDF output in this codebase's conventions (no existing
document-generation code to test against). Confirm by running the API and calling the certificate
endpoint once real data exists — this is folded into Task 10's integration test instead, which
asserts the response is a non-empty `application/pdf` body rather than inspecting PDF internals.

- [ ] **Step 8: Commit**

```bash
git add src/PSMPE.Portal.Application/Events/ICertificateGenerator.cs \
  src/PSMPE.Portal.Infrastructure/Services/EventCertificateGenerator.cs \
  src/PSMPE.Portal.WebAPI/Program.cs \
  src/PSMPE.Portal.Infrastructure/DependencyInjection.cs \
  src/PSMPE.Portal.Infrastructure/PSMPE.Portal.Infrastructure.csproj
git commit -m "feat: add QuestPDF-based CPD certificate generation"
```

---

## 10. `EventsController`

**Files:**
- Create: `src/PSMPE.Portal.WebAPI/Controllers/EventsController.cs`
- Test: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Events/EventsControllerTests.cs`

- [ ] **Step 1: Write the failing integration tests**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Events.Dtos;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Events;

public class EventsControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly HttpClient _client;

    public EventsControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        var scope = factory.Services.CreateScope();
        _userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private HttpRequestMessage Authorized(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task CreateEvent_WithoutEventsManage_Returns403()
    {
        var memberToken = await _client.RegisterAndLoginAsync("Member Only");
        var request = Authorized(HttpMethod.Post, "/api/events", memberToken);
        request.Content = JsonContent.Create(new
        {
            title = "Blocked", description = (string?)null, chapter = (string?)null, venue = (string?)null,
            startsAt = DateTimeOffset.UtcNow.AddDays(5), endsAt = DateTimeOffset.UtcNow.AddDays(5).AddHours(3),
            capacity = (int?)null, fee = 0m,
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_AsAdmin_Succeeds_WithCpdUnitsNull()
    {
        var (_, adminToken) = await _client.CreatePrivilegedUserAsync(_userManager, RoleNames.Admin);
        var request = Authorized(HttpMethod.Post, "/api/events", adminToken);
        request.Content = JsonContent.Create(new
        {
            title = "National Convention", description = "Annual gathering", chapter = (string?)null, venue = "SMX",
            startsAt = DateTimeOffset.UtcNow.AddDays(30), endsAt = DateTimeOffset.UtcNow.AddDays(31),
            capacity = 500, fee = 1000m,
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<EventDto>();
        Assert.Null(body!.CpdUnits);
    }

    /// <summary>
    /// End-to-end: register -> submit event payment -> admin verifies -> self check-in -> submit
    /// evaluation -> admin sets CPD units -> the member's CPD total reflects it -> certificate
    /// downloads as a PDF. Exercises every controller action in this file against real HTTP with
    /// real [Authorize]/[RequirePermission] enforcement.
    /// </summary>
    [Fact]
    public async Task FullEventLifecycle_EndsWithCorrectCreditAndDownloadableCertificate()
    {
        var (_, adminToken) = await _client.CreatePrivilegedUserAsync(_userManager, RoleNames.Admin);
        var createRequest = Authorized(HttpMethod.Post, "/api/events", adminToken);
        createRequest.Content = JsonContent.Create(new
        {
            title = "Plumbing Code Seminar", description = (string?)null, chapter = "NCR", venue = "NCR Chapter Hall",
            startsAt = DateTimeOffset.UtcNow.AddSeconds(-5), endsAt = DateTimeOffset.UtcNow.AddHours(3),
            capacity = (int?)null, fee = 500m,
        });
        var createResponse = await _client.SendAsync(createRequest);
        var @event = await createResponse.Content.ReadFromJsonAsync<EventDto>();

        var memberToken = await _client.RegisterAndLoginAsync("CPD Tester");
        var registerResponse = await _client.SendAsync(Authorized(HttpMethod.Post, $"/api/events/{@event!.Id}/register", memberToken));
        var registration = await registerResponse.Content.ReadFromJsonAsync<EventRegistrationDto>();

        var paymentRequest = Authorized(HttpMethod.Post, $"/api/events/registrations/{registration!.Id}/payment", memberToken);
        paymentRequest.Content = JsonContent.Create(new { amount = 500m, referenceNo = "REF-1", paidOn = DateOnly.FromDateTime(DateTime.UtcNow) });
        var paymentResponse = await _client.SendAsync(paymentRequest);
        Assert.Equal(HttpStatusCode.OK, paymentResponse.StatusCode);
        var payment = await paymentResponse.Content.ReadFromJsonAsync<PSMPE.Portal.Application.Payments.Dtos.PaymentDto>();

        var proofContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([1, 2, 3]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        proofContent.Add(fileContent, "file", "proof.png");
        var proofRequest = Authorized(HttpMethod.Post, $"/api/payments/{payment!.Id}/proof", memberToken);
        proofRequest.Content = proofContent;
        await _client.SendAsync(proofRequest);

        var verifyResponse = await _client.SendAsync(Authorized(HttpMethod.Post, $"/api/payments/{payment.Id}/verify", adminToken));
        Assert.Equal(HttpStatusCode.NoContent, verifyResponse.StatusCode);

        var checkInResponse = await _client.SendAsync(Authorized(HttpMethod.Post, $"/api/events/registrations/{registration.Id}/check-in", memberToken));
        Assert.Equal(HttpStatusCode.NoContent, checkInResponse.StatusCode);

        var evalRequest = Authorized(HttpMethod.Post, $"/api/events/registrations/{registration.Id}/evaluation", memberToken);
        evalRequest.Content = JsonContent.Create(new { rating = 5, comments = "Excellent" });
        var evalResponse = await _client.SendAsync(evalRequest);
        Assert.Equal(HttpStatusCode.NoContent, evalResponse.StatusCode);

        // Certificate not yet available - CpdUnits is still TBD.
        var tooEarly = await _client.SendAsync(Authorized(HttpMethod.Get, $"/api/events/registrations/{registration.Id}/certificate", memberToken));
        Assert.Equal(HttpStatusCode.BadRequest, tooEarly.StatusCode);

        var updateRequest = Authorized(HttpMethod.Put, $"/api/events/{@event.Id}", adminToken);
        updateRequest.Content = JsonContent.Create(new
        {
            title = @event.Title, description = @event.Description, chapter = @event.Chapter, venue = @event.Venue,
            startsAt = @event.StartsAt, endsAt = @event.EndsAt, capacity = @event.Capacity, fee = @event.Fee, cpdUnits = 4,
        });
        await _client.SendAsync(updateRequest);

        var cpdResponse = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/members/me/cpd", memberToken));
        var summary = await cpdResponse.Content.ReadFromJsonAsync<MyCpdSummaryDto>();
        Assert.Equal(4, summary!.TotalUnits);

        var certificateResponse = await _client.SendAsync(Authorized(HttpMethod.Get, $"/api/events/registrations/{registration.Id}/certificate", memberToken));
        Assert.Equal(HttpStatusCode.OK, certificateResponse.StatusCode);
        Assert.Equal("application/pdf", certificateResponse.Content.Headers.ContentType?.MediaType);
        var pdfBytes = await certificateResponse.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(pdfBytes);
    }

    [Fact]
    public async Task Roster_WithoutEventsView_Returns403()
    {
        var (_, adminToken) = await _client.CreatePrivilegedUserAsync(_userManager, RoleNames.Admin);
        var createRequest = Authorized(HttpMethod.Post, "/api/events", adminToken);
        createRequest.Content = JsonContent.Create(new
        {
            title = "Roster Test Event", description = (string?)null, chapter = (string?)null, venue = (string?)null,
            startsAt = DateTimeOffset.UtcNow.AddDays(1), endsAt = DateTimeOffset.UtcNow.AddDays(1).AddHours(2),
            capacity = (int?)null, fee = 0m,
        });
        var created = await (await _client.SendAsync(createRequest)).Content.ReadFromJsonAsync<EventDto>();
        var memberToken = await _client.RegisterAndLoginAsync("No Roster Access");

        var response = await _client.SendAsync(Authorized(HttpMethod.Get, $"/api/events/{created!.Id}/roster", memberToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter EventsControllerTests`
Expected: FAIL — `EventsController` doesn't exist, so every request 404s.

- [ ] **Step 3: Add `GetRegistrationByIdAsync` to `IEventService`**

The certificate endpoint (Step 4 below) needs to look up a registration by its own id regardless of
which member it belongs to, for an admin caller — `GetMyCpdAsync` only covers the caller's own
registrations. Add to `IEventService.cs`:

```csharp
    /// <summary>Admin-only lookup by registration id, regardless of which member it belongs to -
    /// used by the certificate endpoint when the caller holds Events.Manage.</summary>
    Task<EventRegistrationDto?> GetRegistrationByIdAsync(Guid registrationId, CancellationToken cancellationToken = default);
```

Add to `EventService.cs`:

```csharp
    public async Task<EventRegistrationDto?> GetRegistrationByIdAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        var registration = await db.EventRegistrations
            .Include(r => r.Event)
            .Include(r => r.Member)
            .FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken);
        if (registration is null)
        {
            return null;
        }

        var payment = await db.Payments.FirstOrDefaultAsync(p => p.EventRegistrationId == registrationId, cancellationToken);
        return ToDto(registration, registration.Event, registration.Member, payment);
    }
```

- [ ] **Step 4: Implement `EventsController`**

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Events;
using PSMPE.Portal.Application.Events.Dtos;
using PSMPE.Portal.Application.Payments;
using PSMPE.Portal.Application.Payments.Dtos;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Authorization;

namespace PSMPE.Portal.WebAPI.Controllers;

/// <summary>
/// PSMPE events and CPD credit tracking. See openspec/changes/add-events-cpd-tracker for the full
/// design. Payment proof upload, verification and rejection are NOT here - they reuse the existing
/// /api/payments/{id}/proof, /verify and /reject endpoints unchanged, since those already authorize
/// by payment ownership rather than by Kind.
/// </summary>
[ApiController]
[Authorize]
public class EventsController(IEventService eventService, IPaymentService paymentService, ICertificateGenerator certificateGenerator) : ControllerBase
{
    [HttpGet("api/events")]
    public async Task<ActionResult<PagedResult<EventDto>>> GetAll(
        int page = 1, int pageSize = 20, string? search = null, string? chapter = null, bool upcomingOnly = false,
        CancellationToken cancellationToken = default) =>
        Ok(await eventService.GetAllAsync(page, pageSize, search, chapter, upcomingOnly, cancellationToken));

    [HttpGet("api/events/{id:guid}")]
    public async Task<ActionResult<EventDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var @event = await eventService.GetByIdAsync(id, cancellationToken);
        return @event is null ? NotFound() : Ok(@event);
    }

    [HttpPost("api/events")]
    [RequirePermission(Permissions.Events.Manage)]
    public async Task<ActionResult<EventDto>> Create(CreateEventRequest request, CancellationToken cancellationToken)
    {
        var result = await eventService.CreateAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToErrorResult(result);
    }

    [HttpPut("api/events/{id:guid}")]
    [RequirePermission(Permissions.Events.Manage)]
    public async Task<ActionResult<EventDto>> Update(Guid id, UpdateEventRequest request, CancellationToken cancellationToken)
    {
        var result = await eventService.UpdateAsync(id, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToErrorResult(result);
    }

    [HttpGet("api/events/{eventId:guid}/roster")]
    [RequirePermission(Permissions.Events.View)]
    public async Task<ActionResult<IReadOnlyList<EventRegistrationDto>>> GetRoster(Guid eventId, CancellationToken cancellationToken)
    {
        var result = await eventService.GetRosterAsync(eventId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToErrorResult(result);
    }

    [HttpPost("api/events/{eventId:guid}/register")]
    public async Task<ActionResult<EventRegistrationDto>> Register(Guid eventId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();

        var result = await eventService.RegisterAsync(userId.Value, eventId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToErrorResult(result);
    }

    [HttpPost("api/events/registrations/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();

        return ToActionResult(await eventService.CancelRegistrationAsync(userId.Value, id, cancellationToken));
    }

    [HttpPost("api/events/registrations/{id:guid}/payment")]
    public async Task<ActionResult<PaymentDto>> SubmitPayment(Guid id, SubmitPaymentRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();

        var result = await paymentService.SubmitForEventRegistrationAsync(userId.Value, id, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToErrorResult(result);
    }

    [HttpPost("api/events/registrations/{id:guid}/check-in")]
    public async Task<IActionResult> CheckIn(Guid id, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();

        return ToActionResult(await eventService.CheckInAsync(userId.Value, id, cancellationToken));
    }

    public record SetAttendanceRequest(bool Attended);

    [HttpPost("api/events/registrations/{id:guid}/attendance")]
    [RequirePermission(Permissions.Events.Manage)]
    public async Task<IActionResult> SetAttendance(Guid id, SetAttendanceRequest request, CancellationToken cancellationToken)
    {
        var adminUserId = CurrentUserId;
        if (adminUserId is null) return Unauthorized();

        return ToActionResult(await eventService.SetAttendanceAsync(id, request.Attended, adminUserId.Value, cancellationToken));
    }

    public record SubmitEvaluationRequest(int Rating, string? Comments);

    [HttpPost("api/events/registrations/{id:guid}/evaluation")]
    public async Task<IActionResult> SubmitEvaluation(Guid id, SubmitEvaluationRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();

        return ToActionResult(await eventService.SubmitEvaluationAsync(userId.Value, id, request.Rating, request.Comments, cancellationToken));
    }

    [HttpGet("api/members/me/cpd")]
    public async Task<ActionResult<MyCpdSummaryDto>> GetMyCpd(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();

        return Ok(await eventService.GetMyCpdAsync(userId.Value, cancellationToken));
    }

    [HttpGet("api/events/registrations/{id:guid}/certificate")]
    public async Task<IActionResult> GetCertificate(Guid id, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();

        var isAdmin = User.HasClaim(Permissions.ClaimType, Permissions.Events.Manage);
        var rosterLookup = isAdmin ? null : (Guid?)userId;

        // Reuses GetMyCpdAsync/GetRosterAsync-shaped data rather than a new eligibility method -
        // load the caller's own CPD summary (or, for an admin, the roster) and find the matching
        // registration; this keeps eligibility logic in exactly one place (CpdCredit.For, already
        // exercised by both of those paths).
        EventRegistrationDto? registration;
        if (isAdmin)
        {
            registration = await eventService.GetRegistrationByIdAsync(id, cancellationToken);
        }
        else
        {
            var summary = await eventService.GetMyCpdAsync(userId.Value, cancellationToken);
            registration = summary.Registrations.FirstOrDefault(r => r.Id == id);
        }

        if (registration is null)
        {
            return NotFound();
        }
        if (registration.CreditUnits is null)
        {
            return BadRequest(new { message = "This registration hasn't earned CPD credit yet - either the evaluation isn't submitted, or the event's CPD units haven't been set." });
        }

        var pdf = certificateGenerator.Generate(new CertificateData(
            registration.MemberName, registration.EventTitle, registration.EventStartsAt,
            registration.CreditUnits.Value, registration.Id));
        return File(pdf, "application/pdf", $"CPD-Certificate-{registration.Id}.pdf");
    }

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    private IActionResult ToActionResult(Result result)
    {
        if (result.Succeeded) return NoContent();
        return result.ErrorType switch
        {
            ResultErrorType.NotFound => NotFound(new { message = result.Error }),
            ResultErrorType.Forbidden => Forbid(),
            ResultErrorType.Conflict => Conflict(new { message = result.Error }),
            _ => BadRequest(new { message = result.Error }),
        };
    }

    private ActionResult ToErrorResult(Result result) => result.ErrorType switch
    {
        ResultErrorType.NotFound => NotFound(new { message = result.Error }),
        ResultErrorType.Forbidden => Forbid(),
        ResultErrorType.Conflict => Conflict(new { message = result.Error }),
        _ => BadRequest(new { message = result.Error }),
    };
}
```

- [ ] **Step 5: Register `EventsController`'s dependencies and re-run**

`IEventService`, `IPaymentService`, and `ICertificateGenerator` are already registered in DI
(Tasks 9 Step 5, and `IPaymentService` pre-existing) — no further DI changes needed.

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter EventsControllerTests`
Expected: PASS (4 tests, including the full lifecycle test).

- [ ] **Step 6: Full solution test run**

Run: `dotnet test src/PSMPE.Portal.sln`
Expected: PASS — confirms nothing in Task 8's `PaymentService` changes broke existing membership
payment integration tests either.

- [ ] **Step 7: Commit**

```bash
git add src/PSMPE.Portal.WebAPI/Controllers/EventsController.cs \
  src/PSMPE.Portal.Application/Events/ \
  tests/PSMPE.Portal.WebAPI.IntegrationTests/Events/EventsControllerTests.cs
git commit -m "feat: add EventsController with full registration-to-certificate flow"
```

---

## 11. Frontend — `eventApi.ts`

**Files:**
- Create: `apps/web/src/core/api/endpoints/eventApi.ts`

- [ ] **Step 1: Create the API client wrapper**

```ts
import { apiClient } from '../apiClient'
import type { PagedResult } from './adminApi'
import type { SubmitPaymentRequest, Payment } from './paymentApi'

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

export interface Event {
  id: string
  title: string
  description: string | null
  chapter: string | null
  venue: string | null
  startsAt: string
  endsAt: string
  capacity: number | null
  registeredCount: number
  fee: number
  cpdUnits: number | null
}

export interface CreateEventRequest {
  title: string
  description: string | null
  chapter: string | null
  venue: string | null
  startsAt: string
  endsAt: string
  capacity: number | null
  fee: number
}

export interface UpdateEventRequest extends CreateEventRequest {
  cpdUnits: number | null
}

export interface EventRegistration {
  id: string
  eventId: string
  eventTitle: string
  eventStartsAt: string
  memberId: string
  memberName: string
  membershipNo: string | null
  status: EventRegistrationStatusValue
  attendedAt: string | null
  isSelfCheckIn: boolean | null
  evaluationRating: number | null
  evaluationComments: string | null
  evaluationSubmittedAt: string | null
  creditUnits: number | null
  paymentId: string | null
  paymentStatus: string | null
  paymentRejectedReason: string | null
}

export interface MyCpdSummary {
  totalUnits: number
  registrations: EventRegistration[]
}

export const eventApi = {
  getEvents: (params: { page?: number; pageSize?: number; search?: string; chapter?: string; upcomingOnly?: boolean } = {}) =>
    apiClient.get<PagedResult<Event>>('/api/events', { params }).then((res) => res.data),

  getEvent: (id: string) => apiClient.get<Event>(`/api/events/${id}`).then((res) => res.data),

  createEvent: (request: CreateEventRequest) =>
    apiClient.post<Event>('/api/events', request).then((res) => res.data),

  updateEvent: (id: string, request: UpdateEventRequest) =>
    apiClient.put<Event>(`/api/events/${id}`, request).then((res) => res.data),

  getRoster: (eventId: string) =>
    apiClient.get<EventRegistration[]>(`/api/events/${eventId}/roster`).then((res) => res.data),

  register: (eventId: string) =>
    apiClient.post<EventRegistration>(`/api/events/${eventId}/register`).then((res) => res.data),

  cancelRegistration: (registrationId: string) =>
    apiClient.post(`/api/events/registrations/${registrationId}/cancel`).then((res) => res.data),

  submitPayment: (registrationId: string, request: SubmitPaymentRequest) =>
    apiClient.post<Payment>(`/api/events/registrations/${registrationId}/payment`, request).then((res) => res.data),

  checkIn: (registrationId: string) =>
    apiClient.post(`/api/events/registrations/${registrationId}/check-in`).then((res) => res.data),

  setAttendance: (registrationId: string, attended: boolean) =>
    apiClient.post(`/api/events/registrations/${registrationId}/attendance`, { attended }).then((res) => res.data),

  submitEvaluation: (registrationId: string, rating: number, comments: string | null) =>
    apiClient.post(`/api/events/registrations/${registrationId}/evaluation`, { rating, comments }).then((res) => res.data),

  getMyCpd: () => apiClient.get<MyCpdSummary>('/api/members/me/cpd').then((res) => res.data),

  /** Blob fetch, same pattern as paymentApi.fetchProofUrl / uploadApi.fetchMyReceiptUrl - the
   *  request needs the bearer token, which a plain <a href> can't carry. */
  fetchCertificateUrl: async (registrationId: string): Promise<{ url: string; contentType: string } | null> => {
    try {
      const response = await apiClient.get(`/api/events/registrations/${registrationId}/certificate`, { responseType: 'blob' })
      return { url: URL.createObjectURL(response.data), contentType: response.data.type }
    } catch {
      return null
    }
  },
}
```

- [ ] **Step 2: Type-check**

Run: `cd apps/web && npm run build`
Expected: succeeds — this file has no consumers yet, so this just confirms it's syntactically and
type-correct in isolation.

- [ ] **Step 3: Commit**

```bash
git add apps/web/src/core/api/endpoints/eventApi.ts
git commit -m "feat: add eventApi client"
```

---

## 12. Frontend — `EventsPage`

**Files:**
- Create: `apps/web/src/core/pages/EventsPage.tsx`
- Create: `apps/web/src/core/pages/events/EventFormModal.tsx`
- Create: `apps/web/src/core/pages/events/EventRegistrationCard.tsx`

One page serves everyone: a searchable, chapter-filterable event list (per this codebase's
search+filter convention for every list) with a "Register" flow for members, plus a "Create Event"
button and per-row "Manage" link visible only when the caller holds `events:manage`.

- [ ] **Step 1: Create `EventRegistrationCard`** — the per-event registration/payment/status widget
shown when a user expands an event they're eligible to register for or already registered in.

```tsx
import { useCallback, useEffect, useState } from 'react'
import { LuCheck, LuUpload } from 'react-icons/lu'
import { eventApi, type Event, type EventRegistration } from '../../api/endpoints/eventApi'
import { describeError } from '../../utils/apiError'
import { StandardButton } from '../../../integrations/template/components/shared/StandardButton'

interface EventRegistrationCardProps {
  event: Event
  registration: EventRegistration | null
  onChanged: () => void | Promise<void>
}

const peso = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP' })

export function EventRegistrationCard({ event, registration, onChanged }: EventRegistrationCardProps) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [amount, setAmount] = useState(String(event.fee))
  const [referenceNo, setReferenceNo] = useState('')
  const [rating, setRating] = useState(5)
  const [comments, setComments] = useState('')

  const run = useCallback(
    async (action: () => Promise<unknown>) => {
      setBusy(true)
      setError(null)
      try {
        await action()
        await onChanged()
      } catch (err) {
        setError(describeError(err, 'That action could not be completed. Please try again.'))
      } finally {
        setBusy(false)
      }
    },
    [onChanged],
  )

  if (!registration) {
    return (
      <div className="flex flex-col gap-2">
        {error && <p className="text-sm font-medium text-danger">{error}</p>}
        <StandardButton loading={busy} loadingLabel="Registering…" onClick={() => run(() => eventApi.register(event.id))}>
          Register {event.fee > 0 ? `(${peso.format(event.fee)})` : '(Free)'}
        </StandardButton>
      </div>
    )
  }

  if (registration.status === 'Registered') {
    return (
      <div className="flex flex-col gap-3">
        {error && <p className="text-sm font-medium text-danger">{error}</p>}
        <div className="grid grid-cols-2 gap-3">
          <input className="form-input" type="number" min="0" step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="Amount paid" />
          <input className="form-input" value={referenceNo} onChange={(e) => setReferenceNo(e.target.value)} placeholder="Reference no. (optional)" />
        </div>
        <StandardButton
          icon={LuUpload}
          loading={busy}
          loadingLabel="Submitting…"
          onClick={() =>
            run(() =>
              eventApi.submitPayment(registration.id, {
                amount: Number(amount),
                referenceNo: referenceNo.trim() || null,
                paidOn: new Date().toISOString().slice(0, 10),
              }),
            )
          }
        >
          Submit Payment
        </StandardButton>
      </div>
    )
  }

  if (registration.status === 'PaymentSubmitted') {
    return <p className="text-sm text-default-600 bg-default-100 rounded-lg px-3 py-2">Payment submitted — waiting for verification.</p>
  }

  if (registration.status === 'Rejected') {
    return (
      <p className="text-sm text-danger bg-danger/10 rounded-lg px-3 py-2">
        Payment rejected{registration.paymentRejectedReason ? `: ${registration.paymentRejectedReason}` : '.'} Register again to resubmit.
      </p>
    )
  }

  if (registration.status === 'PaymentVerified') {
    return (
      <div className="flex flex-col gap-2">
        {error && <p className="text-sm font-medium text-danger">{error}</p>}
        <StandardButton icon={LuCheck} loading={busy} loadingLabel="Checking in…" onClick={() => run(() => eventApi.checkIn(registration.id))}>
          Check In
        </StandardButton>
      </div>
    )
  }

  if (registration.status === 'Attended') {
    return (
      <div className="flex flex-col gap-3">
        {error && <p className="text-sm font-medium text-danger">{error}</p>}
        <div>
          <label htmlFor="eval-rating" className="block text-sm font-medium text-default-900 mb-1">Rating (1-5)</label>
          <input id="eval-rating" className="form-input w-24" type="number" min="1" max="5" value={rating} onChange={(e) => setRating(Number(e.target.value))} />
        </div>
        <textarea className="form-input" placeholder="Comments (optional)" value={comments} onChange={(e) => setComments(e.target.value)} />
        <StandardButton
          loading={busy}
          loadingLabel="Submitting…"
          onClick={() => run(() => eventApi.submitEvaluation(registration.id, rating, comments.trim() || null))}
        >
          Submit Evaluation
        </StandardButton>
      </div>
    )
  }

  // EvaluationSubmitted
  return (
    <p className="text-sm text-success bg-success/10 rounded-lg px-3 py-2">
      Completed{registration.creditUnits !== null ? ` — ${registration.creditUnits} CPD unit(s) earned.` : ' — CPD units pending (TBD).'}
    </p>
  )
}
```

- [ ] **Step 2: Create `EventFormModal`** — Admin-only create/edit modal, including the "Set CPD
units" field.

```tsx
import { useState } from 'react'
import { eventApi, type Event } from '../../api/endpoints/eventApi'
import { describeError } from '../../utils/apiError'
import { Chapters } from '../../types/member'
import { StandardButton } from '../../../integrations/template/components/shared/StandardButton'

interface EventFormModalProps {
  event: Event | null
  onClose: () => void
  onSaved: () => void | Promise<void>
}

export function EventFormModal({ event, onClose, onSaved }: EventFormModalProps) {
  const [title, setTitle] = useState(event?.title ?? '')
  const [description, setDescription] = useState(event?.description ?? '')
  const [chapter, setChapter] = useState(event?.chapter ?? '')
  const [venue, setVenue] = useState(event?.venue ?? '')
  const [startsAt, setStartsAt] = useState(event?.startsAt.slice(0, 16) ?? '')
  const [endsAt, setEndsAt] = useState(event?.endsAt.slice(0, 16) ?? '')
  const [capacity, setCapacity] = useState(event?.capacity?.toString() ?? '')
  const [fee, setFee] = useState(event?.fee.toString() ?? '0')
  const [cpdUnits, setCpdUnits] = useState(event?.cpdUnits?.toString() ?? '')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleSave = async () => {
    setSaving(true)
    setError(null)
    try {
      const payload = {
        title,
        description: description.trim() || null,
        chapter: chapter || null,
        venue: venue.trim() || null,
        startsAt: new Date(startsAt).toISOString(),
        endsAt: new Date(endsAt).toISOString(),
        capacity: capacity ? Number(capacity) : null,
        fee: Number(fee),
      }
      if (event) {
        await eventApi.updateEvent(event.id, { ...payload, cpdUnits: cpdUnits ? Number(cpdUnits) : null })
      } else {
        await eventApi.createEvent(payload)
      }
      await onSaved()
      onClose()
    } catch (err) {
      setError(describeError(err, 'Could not save this event. Please try again.'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      <div className="card w-full max-w-lg">
        <div className="card-header"><h6 className="card-title">{event ? 'Edit Event' : 'Create Event'}</h6></div>
        <div className="card-body flex flex-col gap-3">
          {error && <p className="text-sm font-medium text-danger">{error}</p>}
          <input className="form-input" placeholder="Title" value={title} onChange={(e) => setTitle(e.target.value)} />
          <textarea className="form-input" placeholder="Description" value={description} onChange={(e) => setDescription(e.target.value)} />
          <div className="grid grid-cols-2 gap-3">
            <select className="form-input" value={chapter} onChange={(e) => setChapter(e.target.value)}>
              <option value="">National (all chapters)</option>
              {Chapters.All.map((c) => <option key={c} value={c}>{c}</option>)}
            </select>
            <input className="form-input" placeholder="Venue" value={venue} onChange={(e) => setVenue(e.target.value)} />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <input className="form-input" type="datetime-local" value={startsAt} onChange={(e) => setStartsAt(e.target.value)} />
            <input className="form-input" type="datetime-local" value={endsAt} onChange={(e) => setEndsAt(e.target.value)} />
          </div>
          <div className="grid grid-cols-3 gap-3">
            <input className="form-input" type="number" min="1" placeholder="Capacity" value={capacity} onChange={(e) => setCapacity(e.target.value)} />
            <input className="form-input" type="number" min="0" step="0.01" placeholder="Fee" value={fee} onChange={(e) => setFee(e.target.value)} />
            {event && (
              <input className="form-input" type="number" min="0" placeholder="CPD units (TBD)" value={cpdUnits} onChange={(e) => setCpdUnits(e.target.value)} />
            )}
          </div>
        </div>
        <div className="card-footer flex justify-end gap-2">
          <button type="button" className="btn border border-default-200" onClick={onClose}>Cancel</button>
          <StandardButton loading={saving} loadingLabel="Saving…" onClick={handleSave}>Save</StandardButton>
        </div>
      </div>
    </div>
  )
}
```

Note: `Chapters` is imported from `apps/web/src/core/types/member.ts` — confirm its exact exported
shape there (it should mirror `Domain/Enums/Chapters.cs`'s constants) before wiring this up; adjust
the import path/name only if it differs from `Chapters.All`.

- [ ] **Step 3: Create `EventsPage`**

```tsx
import { useCallback, useEffect, useState } from 'react'
import { LuCalendarClock, LuMapPin, LuPlus } from 'react-icons/lu'
import { eventApi, type Event, type EventRegistration } from '../api/endpoints/eventApi'
import { describeError } from '../utils/apiError'
import { useAuth } from '../auth/useAuth'
import { Permissions } from '../types/auth'
import { Chapters } from '../types/member'
import { EventFormModal } from './events/EventFormModal'
import { EventRegistrationCard } from './events/EventRegistrationCard'
import { StandardButton } from '../../integrations/template/components/shared/StandardButton'

const PAGE_SIZE = 20

export function EventsPage() {
  const { user } = useAuth()
  const canManage = user?.permissions.includes(Permissions.Events.Manage) ?? false

  const [events, setEvents] = useState<Event[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [page, setPage] = useState(1)
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [chapterFilter, setChapterFilter] = useState('')
  const [myCpdRegistrations, setMyCpdRegistrations] = useState<EventRegistration[]>([])
  const [expandedEventId, setExpandedEventId] = useState<string | null>(null)
  const [editingEvent, setEditingEvent] = useState<Event | null | 'new'>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const timer = setTimeout(() => {
      setSearch(searchInput)
      setPage(1)
    }, 350)
    return () => clearTimeout(timer)
  }, [searchInput])

  const load = useCallback(async () => {
    const [eventPage, cpd] = await Promise.all([
      eventApi.getEvents({ page, pageSize: PAGE_SIZE, search: search || undefined, chapter: chapterFilter || undefined }),
      eventApi.getMyCpd().catch(() => ({ totalUnits: 0, registrations: [] })),
    ])
    setEvents(eventPage.items)
    setTotalCount(eventPage.totalCount)
    setMyCpdRegistrations(cpd.registrations)
  }, [page, search, chapterFilter])

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)
    load()
      .catch((err) => { if (!cancelled) setError(describeError(err, 'Could not load events.')) })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [load])

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE))
  const registrationFor = (eventId: string) => myCpdRegistrations.find((r) => r.eventId === eventId) ?? null

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h4 className="text-xl font-semibold">Events</h4>
        {canManage && (
          <StandardButton icon={LuPlus} onClick={() => setEditingEvent('new')}>Create Event</StandardButton>
        )}
      </div>

      <div className="card">
        <div className="card-header flex flex-wrap items-center gap-3">
          <input
            className="form-input max-w-xs"
            placeholder="Search events…"
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
          />
          <select className="form-input max-w-48" value={chapterFilter} onChange={(e) => { setChapterFilter(e.target.value); setPage(1) }}>
            <option value="">All chapters</option>
            {Chapters.All.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>

        {error && <p className="px-6 py-3 text-sm font-medium text-danger">{error}</p>}
        {loading ? (
          <p className="px-6 py-8 text-center text-default-500">Loading…</p>
        ) : events.length === 0 ? (
          <p className="px-6 py-8 text-center text-default-500">No events found.</p>
        ) : (
          <ul className="flex flex-col divide-y divide-default-200">
            {events.map((event) => (
              <li key={event.id} className="px-6 py-4">
                <div className="flex items-start justify-between gap-4">
                  <div className="flex flex-col gap-1">
                    <button type="button" className="text-left font-medium text-default-900 flex items-center gap-2" onClick={() => setExpandedEventId(expandedEventId === event.id ? null : event.id)}>
                      <LuCalendarClock className="size-4 shrink-0" />
                      {event.title}
                    </button>
                    <span className="flex items-center gap-1 text-xs text-default-500">
                      <LuMapPin className="size-3" />
                      {event.chapter ?? 'National'} · {event.venue ?? 'TBA'} · {new Date(event.startsAt).toLocaleDateString()}
                    </span>
                    <span className="text-xs text-default-500">
                      CPD units: {event.cpdUnits ?? 'TBD'} · {event.registeredCount}{event.capacity ? `/${event.capacity}` : ''} registered
                    </span>
                  </div>
                  {canManage && (
                    <div className="flex items-center gap-2 shrink-0">
                      <button type="button" className="btn btn-sm border border-default-200" onClick={() => setEditingEvent(event)}>Edit</button>
                      <a className="btn btn-sm border border-default-200" href={`/admin/events/${event.id}`}>Roster</a>
                    </div>
                  )}
                </div>
                {expandedEventId === event.id && (
                  <div className="mt-3 pl-6 border-l-2 border-default-200">
                    <EventRegistrationCard event={event} registration={registrationFor(event.id)} onChanged={load} />
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}

        <div className="card-footer flex items-center justify-between">
          <span className="text-sm text-default-500">Page {page} of {totalPages} ({totalCount} total)</span>
          <div className="flex items-center gap-1.5">
            <button type="button" className="btn btn-sm border border-default-200 disabled:opacity-50" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
            <button type="button" className="btn btn-sm border border-default-200 disabled:opacity-50" disabled={page >= totalPages} onClick={() => setPage(page + 1)}>Next</button>
          </div>
        </div>
      </div>

      {editingEvent !== null && (
        <EventFormModal
          event={editingEvent === 'new' ? null : editingEvent}
          onClose={() => setEditingEvent(null)}
          onSaved={load}
        />
      )}
    </div>
  )
}
```

Note: `user.permissions` and `Permissions.Events.Manage` — confirm the exact shape of the frontend
auth `user` object and whether a `Permissions` constant object already exists on the frontend (check
`apps/web/src/core/types/auth.ts`) before wiring the `canManage` check; if the frontend doesn't
currently expose granular permissions on the auth context (only `roles`), fall back to checking
`user.roles.includes(Roles.Admin) || user.roles.includes(Roles.SuperAdmin)` instead, matching the
`MembersPage.tsx` `canManageMembers` pattern from Task 12's research.

- [ ] **Step 4: Type-check**

Run: `cd apps/web && npm run build`
Expected: succeeds once the permission-check fallback (if needed) from Step 3's note is applied.

- [ ] **Step 5: Commit**

```bash
git add apps/web/src/core/pages/EventsPage.tsx apps/web/src/core/pages/events/
git commit -m "feat: add EventsPage with search, filter, registration and admin create/edit"
```

---

## 13. Frontend — `EventRosterPage`

**Files:**
- Create: `apps/web/src/core/pages/EventRosterPage.tsx`

Admin-only, one event's full roster: search by member name, per-row attendance override, and a
read view of each registrant's evaluation and payment status.

- [ ] **Step 1: Create the page**

```tsx
import { useCallback, useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { eventApi, type Event, type EventRegistration } from '../api/endpoints/eventApi'
import { describeError } from '../utils/apiError'

export function EventRosterPage() {
  const { eventId } = useParams<{ eventId: string }>()
  const [event, setEvent] = useState<Event | null>(null)
  const [roster, setRoster] = useState<EventRegistration[]>([])
  const [search, setSearch] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  const load = useCallback(async () => {
    if (!eventId) return
    const [loadedEvent, loadedRoster] = await Promise.all([eventApi.getEvent(eventId), eventApi.getRoster(eventId)])
    setEvent(loadedEvent)
    setRoster(loadedRoster)
  }, [eventId])

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)
    load()
      .catch((err) => { if (!cancelled) setError(describeError(err, 'Could not load the roster.')) })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [load])

  const toggleAttendance = async (registration: EventRegistration) => {
    setBusyId(registration.id)
    try {
      await eventApi.setAttendance(registration.id, registration.status !== 'Attended' && registration.status !== 'EvaluationSubmitted')
      await load()
    } catch (err) {
      setError(describeError(err, 'Could not update attendance.'))
    } finally {
      setBusyId(null)
    }
  }

  const filtered = roster.filter((r) => r.memberName.toLowerCase().includes(search.toLowerCase()) || (r.membershipNo ?? '').toLowerCase().includes(search.toLowerCase()))

  if (loading) return <p className="text-default-500">Loading…</p>
  if (!event) return <p className="text-danger">Event not found.</p>

  return (
    <div className="flex flex-col gap-4">
      <div>
        <h4 className="text-xl font-semibold">{event.title}</h4>
        <p className="text-sm text-default-500">
          CPD units: {event.cpdUnits ?? 'TBD'} · {roster.length}{event.capacity ? `/${event.capacity}` : ''} registered
        </p>
      </div>

      <div className="card">
        <div className="card-header">
          <input className="form-input max-w-xs" placeholder="Search by name or membership no…" value={search} onChange={(e) => setSearch(e.target.value)} />
        </div>
        {error && <p className="px-6 py-3 text-sm font-medium text-danger">{error}</p>}
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left border-b border-default-200">
                <th className="p-3">Member</th>
                <th className="p-3">Payment</th>
                <th className="p-3">Attendance</th>
                <th className="p-3">Evaluation</th>
                <th className="p-3">Credit</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((r) => (
                <tr key={r.id} className="border-b border-default-200">
                  <td className="p-3">{r.memberName}{r.membershipNo ? ` (${r.membershipNo})` : ''}</td>
                  <td className="p-3">{r.paymentStatus ?? '—'}</td>
                  <td className="p-3">
                    <button
                      type="button"
                      className="btn btn-sm border border-default-200 disabled:opacity-50"
                      disabled={busyId === r.id || r.status === 'Registered' || r.status === 'PaymentSubmitted' || r.status === 'Rejected'}
                      onClick={() => toggleAttendance(r)}
                    >
                      {r.status === 'Attended' || r.status === 'EvaluationSubmitted' ? 'Attended ✓' : 'Mark attended'}
                    </button>
                  </td>
                  <td className="p-3">{r.evaluationSubmittedAt ? `${r.evaluationRating}/5` : '—'}</td>
                  <td className="p-3">{r.creditUnits ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}
```

- [ ] **Step 2: Type-check**

Run: `cd apps/web && npm run build`
Expected: succeeds.

- [ ] **Step 3: Commit**

```bash
git add apps/web/src/core/pages/EventRosterPage.tsx
git commit -m "feat: add EventRosterPage with search and attendance override"
```

---

## 14. Frontend — `MyCpdPage`

**Files:**
- Create: `apps/web/src/core/pages/MyCpdPage.tsx`

- [ ] **Step 1: Create the page**

```tsx
import { useCallback, useEffect, useState } from 'react'
import { LuAward, LuDownload } from 'react-icons/lu'
import { eventApi, type MyCpdSummary } from '../api/endpoints/eventApi'
import { describeError } from '../utils/apiError'

export function MyCpdPage() {
  const [summary, setSummary] = useState<MyCpdSummary | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [downloadingId, setDownloadingId] = useState<string | null>(null)

  const load = useCallback(() => eventApi.getMyCpd().then(setSummary), [])

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    load()
      .catch((err) => { if (!cancelled) setError(describeError(err, 'Could not load your CPD history.')) })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [load])

  const downloadCertificate = async (registrationId: string) => {
    setDownloadingId(registrationId)
    try {
      const result = await eventApi.fetchCertificateUrl(registrationId)
      if (!result) return
      const link = document.createElement('a')
      link.href = result.url
      link.download = `CPD-Certificate-${registrationId}.pdf`
      link.click()
      URL.revokeObjectURL(result.url)
    } finally {
      setDownloadingId(null)
    }
  }

  if (loading) return <p className="text-default-500">Loading…</p>
  if (error) return <p className="text-danger">{error}</p>
  if (!summary) return null

  return (
    <div className="flex flex-col gap-4">
      <h4 className="text-xl font-semibold">My CPD</h4>

      <div className="card">
        <div className="card-body flex items-center gap-3">
          <LuAward className="size-8 text-warning" />
          <div>
            <p className="text-2xl font-bold">{summary.totalUnits} units</p>
            <p className="text-sm text-default-500">earned across {summary.registrations.filter((r) => r.status === 'EvaluationSubmitted').length} completed events</p>
          </div>
        </div>
      </div>

      <div className="card">
        <ul className="flex flex-col divide-y divide-default-200">
          {summary.registrations.length === 0 && <li className="px-6 py-8 text-center text-default-500">No events yet — register for one on the Events page.</li>}
          {summary.registrations.map((r) => (
            <li key={r.id} className="px-6 py-4 flex items-center justify-between gap-4">
              <div>
                <p className="font-medium text-default-900">{r.eventTitle}</p>
                <p className="text-xs text-default-500">{new Date(r.eventStartsAt).toLocaleDateString()} · {r.status}</p>
              </div>
              <div className="flex items-center gap-3 shrink-0">
                {r.creditUnits !== null ? (
                  <>
                    <span className="text-sm font-medium text-success">{r.creditUnits} unit(s)</span>
                    <button
                      type="button"
                      className="btn btn-sm border border-default-200 flex items-center gap-1"
                      disabled={downloadingId === r.id}
                      onClick={() => downloadCertificate(r.id)}
                    >
                      <LuDownload className="size-3.5" />
                      {downloadingId === r.id ? 'Preparing…' : 'Certificate'}
                    </button>
                  </>
                ) : (
                  <span className="text-xs text-default-500">
                    {r.status === 'EvaluationSubmitted' ? 'CPD units TBD' : 'Not yet completed'}
                  </span>
                )}
              </div>
            </li>
          ))}
        </ul>
      </div>
    </div>
  )
}
```

- [ ] **Step 2: Type-check**

Run: `cd apps/web && npm run build`
Expected: succeeds.

- [ ] **Step 3: Commit**

```bash
git add apps/web/src/core/pages/MyCpdPage.tsx
git commit -m "feat: add MyCpdPage with certificate download"
```

---

## 15. Routing, nav, and removing the mock widget

**Files:**
- Modify: `apps/web/src/core/routes/router.tsx`
- Modify: `apps/web/src/integrations/template/components/layout/SideNav/menu.ts`
- Modify: `apps/web/src/integrations/template/pages/DashboardPage.tsx`
- Delete: `apps/web/src/integrations/template/components/dashboard-previews/EventsPreviewWidget.tsx`

- [ ] **Step 1: Register the three new routes**

In `apps/web/src/core/routes/router.tsx`, add imports for `EventsPage`, `EventRosterPage`,
`MyCpdPage` near the other page imports, then add inside the `AppShell` children array (same
nesting level as `{ path: '/content', element: <ContentListPage /> }`):

```tsx
                  { path: '/events', element: <EventsPage /> },
                  { path: '/my-cpd', element: <MyCpdPage /> },
```

And inside the existing `ProtectedRoute requiredRoles={[Roles.Admin, Roles.SuperAdmin, Roles.Approval]}`
block, alongside `/members`:

```tsx
                      { path: '/admin/events/:eventId', element: <EventRosterPage /> },
```

- [ ] **Step 2: Add nav entries**

In `apps/web/src/integrations/template/components/layout/SideNav/menu.ts`, add two entries (no
`requiredRoles` — every authenticated user can see Events and My CPD, matching the `/content` and
`/profile` entries):

```ts
{
  key: 'Events',
  label: 'Events',
  icon: LuCalendarClock,
  href: '/events',
},
{
  key: 'MyCpd',
  label: 'My CPD',
  icon: LuAward,
  href: '/my-cpd',
},
```

Add the corresponding `LuCalendarClock, LuAward` imports from `react-icons/lu` at the top of the
file if not already present.

- [ ] **Step 3: Remove the mock widget from `DashboardPage`**

In `apps/web/src/integrations/template/pages/DashboardPage.tsx`:
- Delete the line `import { EventsPreviewWidget } from '../components/dashboard-previews/EventsPreviewWidget'`.
- Replace `<EventsPreviewWidget />` with a small real summary card in its place:

```tsx
<UpcomingEventsWidget />
```

Create `apps/web/src/integrations/template/components/dashboard-previews/UpcomingEventsWidget.tsx`
(same directory, replacing the deleted mock, dropping the "Preview" framing since this is now real):

```tsx
import { useEffect, useState } from 'react'
import { LuCalendarClock, LuMapPin } from 'react-icons/lu'
import { eventApi, type Event } from '../../../../core/api/endpoints/eventApi'
import { StatTile } from '../shared/StatTile'

export function UpcomingEventsWidget() {
  const [events, setEvents] = useState<Event[]>([])

  useEffect(() => {
    eventApi.getEvents({ page: 1, pageSize: 4, upcomingOnly: true }).then((res) => setEvents(res.items)).catch(() => setEvents([]))
  }, [])

  return (
    <div className="card h-full">
      <div className="card-header">
        <h6 className="card-title flex items-center gap-2">
          <LuCalendarClock className="size-4 shrink-0" />
          Upcoming Events
        </h6>
      </div>
      <div className="card-body flex flex-col gap-4">
        <StatTile icon={LuCalendarClock} label="Upcoming events" value={events.length} accent="bg-warning/15 text-warning" />
        {events.length === 0 ? (
          <p className="text-sm text-default-500">No upcoming events right now.</p>
        ) : (
          <ul className="flex flex-col">
            {events.map((event) => (
              <li key={event.id} className="flex items-start justify-between gap-3 py-2 border-b border-dashed border-default-200 last:border-b-0">
                <span className="flex flex-col">
                  <span className="text-sm text-default-700 font-medium">{event.title}</span>
                  <span className="flex items-center gap-1 text-xs text-default-400">
                    <LuMapPin className="size-3 shrink-0" />
                    {event.venue ?? event.chapter ?? 'TBA'}
                  </span>
                </span>
                <span className="text-xs text-default-500 shrink-0 whitespace-nowrap">{new Date(event.startsAt).toLocaleDateString()}</span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}
```

Add `import { UpcomingEventsWidget } from '../components/dashboard-previews/UpcomingEventsWidget'`
to `DashboardPage.tsx` in place of the deleted import.

- [ ] **Step 4: Delete the mock widget file**

```bash
git rm apps/web/src/integrations/template/components/dashboard-previews/EventsPreviewWidget.tsx
```

- [ ] **Step 5: Type-check, lint, and manually verify in the browser**

Run: `cd apps/web && npm run build && npm run lint`
Expected: both succeed (0 errors).

Then run the app (`run.bat` or the documented dev workflow), log in as the seeded Admin account,
and manually walk through: Dashboard shows real upcoming events (not the old mock badge) → create
an event → log in as the seeded Member account → register → submit payment → back as Admin, verify
the payment and confirm the roster shows it → as Member, check in and submit the evaluation → as
Admin, set CPD units on the event → as Member, confirm My CPD shows the credit and the certificate
downloads as a real PDF.

- [ ] **Step 6: Commit**

```bash
git add apps/web/src/core/routes/router.tsx \
  apps/web/src/integrations/template/components/layout/SideNav/menu.ts \
  apps/web/src/integrations/template/pages/DashboardPage.tsx \
  apps/web/src/integrations/template/components/dashboard-previews/UpcomingEventsWidget.tsx
git commit -m "feat: wire Events/My CPD routing and nav, replace dashboard mock widget"
```

---

## 16. Final verification and docs

**Files:**
- Modify: `openspecs/events.md` (new file — this codebase keeps one `openspecs/<feature>.md` per
  shipped feature; see `openspecs/members.md`, `openspecs/payments.md` for the pattern)

- [ ] **Step 1: Full backend build and test run**

Run: `dotnet build src/PSMPE.Portal.sln && dotnet test src/PSMPE.Portal.sln --logger "console;verbosity=normal"`
Expected: build succeeds, all tests pass (existing suite + every test added in Tasks 4–10).

- [ ] **Step 2: Full frontend build and lint**

Run: `cd apps/web && npm run build && npm run lint`
Expected: both succeed.

- [ ] **Step 3: Write `openspecs/events.md`**

Following the existing `openspecs/*.md` convention (per this repo's standing practice of keeping
that directory in sync with what's actually shipped), document: the `Event`/`EventRegistration`
entities and status lifecycle, every `/api/events*` and `/api/members/me/cpd` endpoint with its
permission requirement, the computed-not-stored CPD credit rule, and the three open questions from
`proposal.md` still unresolved with the actual PSMPE client (paid-vs-free events, real unit
requirements, cancellation/refunds) — flagged the same way there so this doc doesn't silently claim
more certainty than the feature actually has.

- [ ] **Step 4: Manual browser pass**

If not already done as part of Task 15 Step 5, complete the full walkthrough there now: create
event → register → pay → verify → check in → evaluate → set CPD units → view credit → download
certificate — across both an Admin and a Member session.

- [ ] **Step 5: Commit**

```bash
git add openspecs/events.md
git commit -m "docs: document Events and CPD Tracker API in openspecs/events.md"
```

- [ ] **Step 6: Update `proposal.md`'s Status**

Change the `## Status` line in `openspec/changes/add-events-cpd-tracker/proposal.md` from
"Brainstormed, not yet approved for implementation" to "Implemented — see tasks.md", matching how
`add-payments-domain/proposal.md` records its own completion. Leave "Open Questions For The Client"
in place unchanged — implementation doesn't resolve those; they still need the actual client.

```bash
git add openspec/changes/add-events-cpd-tracker/proposal.md
git commit -m "docs: mark add-events-cpd-tracker proposal as implemented"
```
