# Tasks: add-events-cpd-tracker

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build Event Management and the CPD Credit Tracker together — members register for a
PSMPE event in a chosen modality (Onsite or Online, paid via the existing Payments domain), admins
reconcile per-session attendance from a roster after the event, members submit a post-event
evaluation, and CPD credit is computed at read time as a proration of sessions attended against
whichever modality's unit count applies. Admins manage events (including their sessions/lectures)
and rosters, record on-site cash payments directly, and set each modality's CPD units before or
after the event. Members see a running credit total and can download a certificate once credit is
earned.

**Architecture:** Four new EF Core entities — `Event`, `EventSession` (one row per lecture/segment,
always at least one per event), `EventRegistration` (one row per member per event, carrying `Mode`
and a forward-walking `Status`), and `EventAttendance` (a join row per session a registrant is
confirmed to have attended, replacing any single whole-event attendance flag) — behind a thin
`EventService`, mirroring the existing `Payment` entity's single-row-with-status-enum shape for
`EventRegistration`. `Payment` gains a third `Kind` (`EventRegistration`) and a nullable
`EventRegistrationId` FK; the existing `POST /api/payments/{id}/verify` and `/reject` endpoints are
reused unchanged (they already work off `Payment.Id` alone), only `PaymentService.VerifyAsync` and
`RejectAsync` grow a branch for the new kind, plus two brand new `PaymentService` methods (submit for
an event registration; record a cash payment directly). CPD credit is a computed property, never
stored — there is no scheduler in this codebase for anything to write it, matching how
`MemberDto.IsExpired`/`MemberService.ComputeIsExpired` already work. Certificates are generated on
demand with QuestPDF, not pre-rendered or cached. Full context: `openspec/changes/add-events-cpd-tracker/proposal.md`
and `specs/events/spec.md` in this folder — **read both before starting**.

**Tech Stack:** .NET 8 + EF Core 8 (Npgsql in prod, EF InMemory in Application unit tests) for the
backend; React 19 + Vite + TypeScript + Tailwind for the frontend, plain axios (no react-query), no
frontend test runner (verification is `tsc -b` / `eslint` / manual browser pass). Backend: xUnit
unit tests (`PSMPE.Portal.Application.UnitTests`) and xUnit integration tests
(`PSMPE.Portal.WebAPI.IntegrationTests`, real HTTP via `WebApplicationFactory<Program>`). PDF
generation: QuestPDF (MIT/Community-licensed, not currently referenced anywhere in this codebase —
`Directory.Packages.props` does not exist in this repo, so package versions are pinned directly in
each `.csproj`; `PSMPE.Portal.Application.csproj` already references `SkiaSharp`/`SkiaSharp.NativeAssets.Linux`,
which QuestPDF's rendering pipeline also depends on, so adding QuestPDF itself is low-friction).

**Sequencing:** Tasks 1–3 lay the data model and permissions. Tasks 4–11 build the Application-layer
services end-to-end (event/session CRUD → registration → attendance → evaluation → CPD computation →
payment integration → roster query → certificate), each verified by unit tests. Task 12 wires it all
up behind `EventsController` (plus one new endpoint on the existing `MembersController`) with
integration tests. Tasks 13–17 build the frontend on top of a working API. Task 18 is final
verification and docs.

**Design note carried through every task below:** the existing `PaymentDto.Status`/`Kind` are typed
as raw C# enums, which — since this codebase has no `JsonStringEnumConverter` configured anywhere
(confirmed by search) — actually serialize as **numbers** over the wire, even though
`paymentApi.ts` types them as string literals (`'Submitted'`, `'Verified'`, ...). That mismatch is a
pre-existing issue in `Payment`, out of scope to fix here. **Do not repeat it**: every new DTO field
below that represents an enum (`EventRegistrationStatus`, `EventMode`) is explicitly converted with
`.ToString()` in the mapping code, and every new request DTO that accepts one takes a `string` and
parses it server-side with `Enum.TryParse`, so what actually crosses the wire is always the string
the frontend's union types expect.

---

## 1. Domain entities and DbContext wiring

**Files:**
- Create: `src/PSMPE.Portal.Domain/Entities/Event.cs`
- Create: `src/PSMPE.Portal.Domain/Entities/EventSession.cs`
- Create: `src/PSMPE.Portal.Domain/Entities/EventRegistration.cs`
- Create: `src/PSMPE.Portal.Domain/Entities/EventAttendance.cs`
- Create: `src/PSMPE.Portal.Domain/Enums/EventRegistrationStatus.cs`
- Create: `src/PSMPE.Portal.Domain/Enums/EventMode.cs`
- Modify: `src/PSMPE.Portal.Domain/Enums/PaymentKind.cs`
- Modify: `src/PSMPE.Portal.Domain/Entities/Payment.cs`
- Modify: `src/PSMPE.Portal.Application/Common/Interfaces/IApplicationDbContext.cs`
- Modify: `src/PSMPE.Portal.Infrastructure/Persistence/ApplicationDbContext.cs`
- Modify: `tests/PSMPE.Portal.Application.UnitTests/TestSupport/TestDbContext.cs`

Pure data classes and DI plumbing — no meaningful behavior to TDD here; verification is a
successful build.

- [ ] **Step 1: Create the `EventRegistrationStatus` and `EventMode` enums**

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

```csharp
namespace PSMPE.Portal.Domain.Enums;

/// <summary>
/// Which of an event's two independently-accredited CPD unit values (Event.CpdUnitsOnsite /
/// Event.CpdUnitsOnline) applies to a given registration's credit. Chosen by the member at
/// registration time - see add-events-cpd-tracker/proposal.md's "CPD units are tracked per
/// modality" decision.
/// </summary>
public enum EventMode
{
    Onsite,
    Online,
}
```

- [ ] **Step 2: Create the `Event` entity**

```csharp
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
```

- [ ] **Step 3: Create the `EventSession` entity**

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
}
```

- [ ] **Step 4: Create the `EventRegistration` entity**

```csharp
namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// One row per member per event - registration, payment progress, attendance and evaluation all
/// live on this single row via Status, mirroring Payment's single-row-with-status-enum shape (see
/// add-events-cpd-tracker/proposal.md). Mode is chosen at registration and decides which of
/// Event.CpdUnitsOnsite/CpdUnitsOnline applies to this registration's credit. CPD credit itself is
/// deliberately NOT a field here - it's computed from Status + Mode + attendance + Event's unit
/// values at read time (see Application/Events/CpdCredit.cs), so a unit value set or corrected
/// after the fact is instantly correct everywhere with no backfill. Which sessions were attended
/// lives on EventAttendance, not here - there is no AttendedAt/AttendedBy flag on this row.
/// </summary>
public class EventRegistration : BaseEntity
{
    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;

    public Guid MemberId { get; set; }
    public Member Member { get; set; } = null!;

    public EventMode Mode { get; set; }
    public EventRegistrationStatus Status { get; set; } = EventRegistrationStatus.Registered;

    /// <summary>1-5. Fixed field set, not admin-configurable per event, to keep this pass
    /// scoped - see proposal.md's "Not Built".</summary>
    public int? EvaluationRating { get; set; }
    public string? EvaluationComments { get; set; }
    public DateTimeOffset? EvaluationSubmittedAt { get; set; }
}
```

- [ ] **Step 5: Create the `EventAttendance` entity**

```csharp
namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// One row per EventSession a registrant is confirmed to have attended - what "attended" means
/// structurally in this design. Recorded by an Admin during post-event roster reconciliation, never
/// by the member themselves (there is no member self-check-in in this product - see
/// add-events-cpd-tracker/proposal.md). RecordedBy/RecordedAt are an audit trail of who reconciled
/// it and when, mirroring Payment.DecidedByUserId/DecidedAt.
/// </summary>
public class EventAttendance : BaseEntity
{
    public Guid EventRegistrationId { get; set; }
    public EventRegistration EventRegistration { get; set; } = null!;

    public Guid EventSessionId { get; set; }
    public EventSession EventSession { get; set; } = null!;

    public Guid RecordedBy { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 6: Extend `PaymentKind` and `Payment` for event registrations**

In `src/PSMPE.Portal.Domain/Enums/PaymentKind.cs`, add a third case (safe to append - the enum is
stored as text via `HasConversion<string>()`, so there's no ordinal-shift risk for existing rows):

```csharp
namespace PSMPE.Portal.Domain.Enums;

/// <summary>
/// What a payment buys. NewMembership/Renewal both differ in what verifying them does (see
/// PaymentVerification.Apply). EventRegistration differs more sharply: verifying it does not touch
/// MembershipStatus or RenewalDueDate at all, it moves the linked EventRegistration.Status instead
/// (see EventPaymentVerification.Apply) - see add-events-cpd-tracker/proposal.md.
/// </summary>
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
    /// payments have no event. A registration can have more than one Payment row over time (e.g. a
    /// Rejected one followed by a resubmission), same as a member's own NewMembership/Renewal
    /// history - only one may be Submitted or Verified at a time, enforced in PaymentService.</summary>
    public Guid? EventRegistrationId { get; set; }
    public EventRegistration? EventRegistration { get; set; }
```

- [ ] **Step 7: Add the four new `DbSet`s to `IApplicationDbContext`**

In `src/PSMPE.Portal.Application/Common/Interfaces/IApplicationDbContext.cs`, add after
`DbSet<Payment> Payments { get; }`:

```csharp
    DbSet<Event> Events { get; }
    DbSet<EventSession> EventSessions { get; }
    DbSet<EventRegistration> EventRegistrations { get; }
    DbSet<EventAttendance> EventAttendances { get; }
```

- [ ] **Step 8: Add the four new `DbSet`s to `ApplicationDbContext`**

In `src/PSMPE.Portal.Infrastructure/Persistence/ApplicationDbContext.cs`, add after
`public DbSet<Payment> Payments => Set<Payment>();`:

```csharp
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventSession> EventSessions => Set<EventSession>();
    public DbSet<EventRegistration> EventRegistrations => Set<EventRegistration>();
    public DbSet<EventAttendance> EventAttendances => Set<EventAttendance>();
```

- [ ] **Step 9: Add the four new `DbSet`s to `TestDbContext`**

In `tests/PSMPE.Portal.Application.UnitTests/TestSupport/TestDbContext.cs`, add the same four
properties (identical syntax to Step 8) so Application-layer unit tests can seed/assert against all
four tables.

- [ ] **Step 10: Build to confirm everything compiles**

Run: `dotnet build src/PSMPE.Portal.sln`
Expected: build succeeds (0 errors). All four new entities are part of the EF model but have no
table yet — that's Task 2.

- [ ] **Step 11: Commit**

```bash
git add src/PSMPE.Portal.Domain/Entities/Event.cs src/PSMPE.Portal.Domain/Entities/EventSession.cs \
  src/PSMPE.Portal.Domain/Entities/EventRegistration.cs src/PSMPE.Portal.Domain/Entities/EventAttendance.cs \
  src/PSMPE.Portal.Domain/Enums/EventRegistrationStatus.cs src/PSMPE.Portal.Domain/Enums/EventMode.cs \
  src/PSMPE.Portal.Domain/Enums/PaymentKind.cs src/PSMPE.Portal.Domain/Entities/Payment.cs \
  src/PSMPE.Portal.Application/Common/Interfaces/IApplicationDbContext.cs \
  src/PSMPE.Portal.Infrastructure/Persistence/ApplicationDbContext.cs \
  tests/PSMPE.Portal.Application.UnitTests/TestSupport/TestDbContext.cs
git commit -m "feat: add Event, EventSession, EventRegistration and EventAttendance entities"
```

---

## 2. EF configurations and migration

**Files:**
- Create: `src/PSMPE.Portal.Infrastructure/Persistence/Configurations/EventConfiguration.cs`
- Create: `src/PSMPE.Portal.Infrastructure/Persistence/Configurations/EventSessionConfiguration.cs`
- Create: `src/PSMPE.Portal.Infrastructure/Persistence/Configurations/EventRegistrationConfiguration.cs`
- Create: `src/PSMPE.Portal.Infrastructure/Persistence/Configurations/EventAttendanceConfiguration.cs`
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
        builder.Property(e => e.CpdUnitsOnsite).HasPrecision(6, 2);
        builder.Property(e => e.CpdUnitsOnline).HasPrecision(6, 2);

        // The events list filters/sorts on StartsAt; the admin roster looks events up by id only.
        builder.HasIndex(e => e.StartsAt);
    }
}
```

- [ ] **Step 2: Create `EventSessionConfiguration`**

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

- [ ] **Step 3: Create `EventRegistrationConfiguration`**

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
        builder.Property(r => r.Mode).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);

        // The roster query filters on EventId; "one non-cancelled registration per member per
        // event" is enforced in EventService, not by a DB constraint, since Cancelled rows must
        // stay queryable without blocking a fresh registration.
        builder.HasIndex(r => r.EventId);
        builder.HasIndex(r => r.MemberId);

        // Restrict, matching Payment.MemberId - deleting an Event or a Member must not silently
        // take registration history with it. Neither Event nor Member deletion exists in this
        // pass, but the FK still needs an explicit choice.
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

- [ ] **Step 4: Create `EventAttendanceConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class EventAttendanceConfiguration : IEntityTypeConfiguration<EventAttendance>
{
    public void Configure(EntityTypeBuilder<EventAttendance> builder)
    {
        // Defensive: EventService.RecordAttendanceAsync always fully replaces a registration's
        // attendance rows in one call rather than upserting, so this should never fire in
        // practice, but a duplicate (registration, session) pair would silently double-count
        // toward "sessions attended" if it ever did.
        builder.HasIndex(a => new { a.EventRegistrationId, a.EventSessionId }).IsUnique();

        // Cascade - an attendance row has no meaning once its registration is gone, mirroring
        // EventSessionConfiguration's reasoning for Event -> EventSession.
        builder.HasOne(a => a.EventRegistration)
            .WithMany()
            .HasForeignKey(a => a.EventRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict - unlike Event -> EventSession, a session with recorded attendance must not be
        // removable out from under that history. EventService.UpdateAsync checks this explicitly
        // before attempting the delete (see Task 4), so this is a defense-in-depth constraint, not
        // the primary guard.
        builder.HasOne(a => a.EventSession)
            .WithMany()
            .HasForeignKey(a => a.EventSessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 5: Extend `PaymentConfiguration` for the new FK**

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

- [ ] **Step 6: Build to confirm the configurations compile**

Run: `dotnet build src/PSMPE.Portal.sln`
Expected: build succeeds (0 errors).

- [ ] **Step 7: Add the migration**

```bash
dotnet ef migrations add AddEventsAndCpdTracker \
  --project src/PSMPE.Portal.Infrastructure/PSMPE.Portal.Infrastructure.csproj \
  --startup-project src/PSMPE.Portal.WebAPI/PSMPE.Portal.WebAPI.csproj \
  --output-dir Persistence/Migrations
```

Expected: a new `Persistence/Migrations/<timestamp>_AddEventsAndCpdTracker.cs` file is generated.
Open it and confirm it creates four tables (`Events`, `EventSessions`, `EventRegistrations`,
`EventAttendances`) and adds one nullable `EventRegistrationId` column + FK to `Payments` — matching
the configurations above. Confirm `EventSessions.EventId` cascades on delete and
`EventAttendances.EventSessionId` restricts, per Steps 2 and 4. If the generated `Up()` looks
materially different from that (e.g. it tries to touch unrelated tables), stop and re-check Steps
1–5 before proceeding; don't hand-edit the migration to force it to match.

- [ ] **Step 8: Verify against a running database**

Run: `docker compose up -d postgres` (if not already running), then start the API once with
`dotnet run --project src/PSMPE.Portal.WebAPI` and confirm the startup log shows the migration
applying cleanly (this app auto-migrates on startup when `Seed:Enabled` is true — see
`README.md`'s "Migrations and seeding" section). Stop the API afterward (`Ctrl+C`).
Expected: no migration errors in the log; `Events`, `EventSessions`, `EventRegistrations`, and
`EventAttendances` tables exist in Postgres, and `Payments` has a new nullable
`EventRegistrationId` column.

- [ ] **Step 9: Commit**

```bash
git add src/PSMPE.Portal.Infrastructure/Persistence/Configurations/EventConfiguration.cs \
  src/PSMPE.Portal.Infrastructure/Persistence/Configurations/EventSessionConfiguration.cs \
  src/PSMPE.Portal.Infrastructure/Persistence/Configurations/EventRegistrationConfiguration.cs \
  src/PSMPE.Portal.Infrastructure/Persistence/Configurations/EventAttendanceConfiguration.cs \
  src/PSMPE.Portal.Infrastructure/Persistence/Configurations/PaymentConfiguration.cs \
  src/PSMPE.Portal.Infrastructure/Persistence/Migrations/
git commit -m "feat: add EF configurations and migration for events and CPD tracking"
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

## 4. Event CRUD and session management (Application layer)

**Files:**
- Create: `src/PSMPE.Portal.Application/Events/Dtos/EventDto.cs`
- Create: `src/PSMPE.Portal.Application/Events/IEventService.cs`
- Create: `src/PSMPE.Portal.Application/Events/EventService.cs`
- Modify: `src/PSMPE.Portal.Application/DependencyInjection.cs`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs`

- [ ] **Step 1: Create the `EventDto`, `EventSessionDto`, and request records**

```csharp
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
```

- [ ] **Step 2: Create `IEventService` with just the event-management members for now**

(Registration/attendance/evaluation/roster/CPD/certificate members are added in Tasks 5–11 — this
interface grows across the plan rather than being fully declared up front, so each task's tests
compile against only what exists so far.)

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

- [ ] **Step 3: Write the failing tests for validation, session defaulting, and listing**

```csharp
using PSMPE.Portal.Application.Events;
using PSMPE.Portal.Application.Events.Dtos;
using PSMPE.Portal.Application.UnitTests.TestSupport;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Events;

public class EventServiceTests
{
    private static CreateEventRequest ValidCreateRequest(string title = "Water Sanitation Workshop") =>
        new(title, "Cross-connection control", Chapters.Ncr, "PICC", DateTimeOffset.UtcNow.AddDays(10),
            DateTimeOffset.UtcNow.AddDays(10).AddHours(4), Capacity: 100, Fee: 500m);

    [Fact]
    public async Task CreateAsync_ValidRequest_StartsWithBothCpdUnitsNull()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);

        var result = await service.CreateAsync(ValidCreateRequest());

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.CpdUnitsOnsite);
        Assert.Null(result.Value.CpdUnitsOnline);
    }

    [Fact]
    public async Task CreateAsync_CreatesExactlyOneDefaultSessionSpanningTheWholeEvent()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var request = ValidCreateRequest();

        var result = await service.CreateAsync(request);

        var session = Assert.Single(result.Value!.Sessions);
        Assert.Equal(request.StartsAt, session.StartsAt);
        Assert.Equal(request.EndsAt, session.EndsAt);
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
    public async Task UpdateAsync_SetsOneModalitysUnitsWhileTheOtherStaysTbd()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var updateRequest = ToUpdateRequest(created) with { CpdUnitsOnsite = 8m, CpdUnitsOnline = null };

        var result = await service.UpdateAsync(created.Id, updateRequest);

        Assert.True(result.Succeeded);
        Assert.Equal(8m, result.Value!.CpdUnitsOnsite);
        Assert.Null(result.Value.CpdUnitsOnline);
    }

    /// <summary>Matches spec.md's "CPD units are set after the event has already happened" -
    /// nothing about Update gates on StartsAt/EndsAt.</summary>
    [Fact]
    public async Task UpdateAsync_EventAlreadyEnded_CanStillSetCpdUnits()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var pastRequest = ValidCreateRequest() with
        {
            StartsAt = DateTimeOffset.UtcNow.AddDays(-10),
            EndsAt = DateTimeOffset.UtcNow.AddDays(-9),
        };
        var created = (await service.CreateAsync(pastRequest)).Value!;
        var updateRequest = ToUpdateRequest(created) with { CpdUnitsOnsite = 8m, CpdUnitsOnline = 4m };

        var result = await service.UpdateAsync(created.Id, updateRequest);

        Assert.True(result.Succeeded);
        Assert.Equal(8m, result.Value!.CpdUnitsOnsite);
    }

    [Fact]
    public async Task UpdateAsync_AddsAndRemovesSessions()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var defaultSession = created.Sessions.Single();
        var sessions = new List<EventSessionRequest>
        {
            new(defaultSession.Id, "Day 1: Opening", defaultSession.StartsAt, defaultSession.StartsAt.AddHours(2), 1),
            new(null, "Day 1: Cross-Connection Control", defaultSession.StartsAt.AddHours(2), defaultSession.EndsAt, 2),
        };
        var updateRequest = ToUpdateRequest(created) with { Sessions = sessions };

        var result = await service.UpdateAsync(created.Id, updateRequest);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Sessions.Count);
        Assert.Contains(result.Value.Sessions, s => s.Title == "Day 1: Cross-Connection Control");
    }

    [Fact]
    public async Task UpdateAsync_RemovingSessionWithRecordedAttendance_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var sessionId = created.Sessions.Single().Id;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = new EventRegistration { EventId = created.Id, MemberId = member.Id, Mode = EventMode.Onsite };
        db.EventRegistrations.Add(registration);
        await db.SaveChangesAsync();
        db.EventAttendances.Add(new EventAttendance { EventRegistrationId = registration.Id, EventSessionId = sessionId, RecordedBy = Guid.NewGuid() });
        await db.SaveChangesAsync();
        // Replaces the only session with a brand new one, which would drop the attended session.
        var updateRequest = ToUpdateRequest(created) with
        {
            Sessions = [new EventSessionRequest(null, "Replacement Session", created.StartsAt, created.EndsAt, 1)],
        };

        var result = await service.UpdateAsync(created.Id, updateRequest);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpdateAsync_NoSessions_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var updateRequest = ToUpdateRequest(created) with { Sessions = [] };

        var result = await service.UpdateAsync(created.Id, updateRequest);

        Assert.False(result.Succeeded);
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
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        db.EventRegistrations.Add(new EventRegistration
        {
            EventId = created.Id, MemberId = member.Id, Mode = EventMode.Onsite,
            Status = EventRegistrationStatus.Cancelled,
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

    private static UpdateEventRequest ToUpdateRequest(EventDto e) =>
        new(e.Title, e.Description, e.Chapter, e.Venue, e.StartsAt, e.EndsAt, e.Capacity, e.Fee,
            e.CpdUnitsOnsite, e.CpdUnitsOnline,
            e.Sessions.Select(s => new EventSessionRequest(s.Id, s.Title, s.StartsAt, s.EndsAt, s.Order)).ToList());

    internal static async Task<Member> SeedMemberForEventTestsAsync(TestDbContext db, string? email = null)
    {
        email ??= $"{Guid.NewGuid()}@example.com";
        var user = new ApplicationUser { UserName = email, Email = email };
        db.Add(user);
        var member = new Member { UserId = user.Id, User = user, FirstName = "Juan", LastName = "Dela Cruz", Chapter = Chapters.Ncr, MemberType = MemberTypes.Regular };
        db.Members.Add(member);
        await db.SaveChangesAsync();
        return member;
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

public partial class EventService(IApplicationDbContext db) : IEventService
{
    public async Task<PagedResult<EventDto>> GetAllAsync(
        int page, int pageSize, string? search, string? chapter, bool upcomingOnly,
        CancellationToken cancellationToken = default)
    {
        var query = db.Events.Include(e => e.Sessions).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(e => e.Title.Contains(term, StringComparison.OrdinalIgnoreCase));
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
        var @event = await db.Events.Include(e => e.Sessions).FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
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
        var validation = ValidateCore(request.Title, request.StartsAt, request.EndsAt, request.Capacity, request.Fee, request.Chapter);
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
        var validation = ValidateCore(request.Title, request.StartsAt, request.EndsAt, request.Capacity, request.Fee, request.Chapter);
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
                var existing = @event.Sessions.First(s => s.Id == sessionId);
                existing.Title = sessionRequest.Title.Trim();
                existing.StartsAt = sessionRequest.StartsAt;
                existing.EndsAt = sessionRequest.EndsAt;
                existing.Order = sessionRequest.Order;
            }
            else
            {
                @event.Sessions.Add(new EventSession
                {
                    EventId = @event.Id,
                    Title = sessionRequest.Title.Trim(),
                    StartsAt = sessionRequest.StartsAt,
                    EndsAt = sessionRequest.EndsAt,
                    Order = sessionRequest.Order,
                });
            }
        }

        @event.Title = request.Title.Trim();
        @event.Description = request.Description;
        @event.Chapter = request.Chapter;
        @event.Venue = request.Venue;
        @event.StartsAt = request.StartsAt;
        @event.EndsAt = request.EndsAt;
        @event.Capacity = request.Capacity;
        @event.Fee = request.Fee;
        @event.CpdUnitsOnsite = request.CpdUnitsOnsite;
        @event.CpdUnitsOnline = request.CpdUnitsOnline;
        @event.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var registeredCount = await db.EventRegistrations.CountAsync(
            r => r.EventId == id && r.Status != EventRegistrationStatus.Cancelled, cancellationToken);
        return Result<EventDto>.Success(ToDto(@event, registeredCount));
    }

    private static string? ValidateCore(string title, DateTimeOffset startsAt, DateTimeOffset endsAt, int? capacity, decimal fee, string? chapter)
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
            registeredCount, e.Fee, e.CpdUnitsOnsite, e.CpdUnitsOnline,
            e.Sessions.OrderBy(s => s.Order)
                .Select(s => new EventSessionDto(s.Id, s.Title, s.StartsAt, s.EndsAt, s.Order))
                .ToList());
}
```

`EventService` is declared `partial` because Tasks 5–11 each add another `partial class EventService`
block to the same `EventService.cs` file rather than one enormous file — see those tasks' Step 5/6
for the exact placement. (`IEventService` is not partial; each task instead adds new members to the
single interface declaration from Step 2 above.)

- [ ] **Step 6: Register `EventService` in DI**

In `src/PSMPE.Portal.Application/DependencyInjection.cs`, add after
`services.AddScoped<IPaymentService, PaymentService>();`:

```csharp
        services.AddScoped<IEventService, EventService>();
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter EventServiceTests`
Expected: PASS (11 tests).

- [ ] **Step 8: Commit**

```bash
git add src/PSMPE.Portal.Application/Events/ src/PSMPE.Portal.Application/DependencyInjection.cs \
  tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs
git commit -m "feat: add EventService with event and session CRUD"
```

---

## 5. Registration and cancellation

**Files:**
- Create: `src/PSMPE.Portal.Application/Events/Dtos/EventRegistrationDto.cs`
- Modify: `src/PSMPE.Portal.Application/Events/IEventService.cs`
- Modify: `src/PSMPE.Portal.Application/Events/EventService.cs`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs`

- [ ] **Step 1: Create `EventRegistrationDto`**

```csharp
namespace PSMPE.Portal.Application.Events.Dtos;

/// <summary>Mode/Status are strings (enum.ToString()), not the raw enums - see the design note at
/// the top of tasks.md: this is deliberately serialized so the frontend's string literal types
/// actually match what's sent over the wire, unlike PaymentDto.Kind/Status.</summary>
public record EventRegistrationDto(
    Guid Id,
    Guid EventId,
    string EventTitle,
    DateTimeOffset EventStartsAt,
    Guid MemberId,
    string MemberName,
    string? MembershipNo,
    string Mode,
    string Status,
    int SessionsAttended,
    int TotalSessions,
    int? EvaluationRating,
    string? EvaluationComments,
    DateTimeOffset? EvaluationSubmittedAt,
    /// <summary>Null until this registration reaches EvaluationSubmitted with a non-null unit
    /// value for its Mode - see Application/Events/CpdCredit.cs.</summary>
    decimal? CreditUnits);

public record RegisterForEventRequest(string Mode);
```

- [ ] **Step 2: Add the new members to `IEventService`**

```csharp
    Task<Result<EventRegistrationDto>> RegisterAsync(Guid userId, Guid eventId, string mode, CancellationToken cancellationToken = default);

    Task<Result> CancelRegistrationAsync(Guid userId, Guid registrationId, CancellationToken cancellationToken = default);
```

- [ ] **Step 3: Write the failing tests**

Append to `EventServiceTests.cs`:

```csharp
    [Fact]
    public async Task RegisterAsync_ValidMode_CreatesRegistrationInRegisteredStatus()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);

        var result = await service.RegisterAsync(member.UserId, @event.Id, "Online");

        Assert.True(result.Succeeded);
        Assert.Equal("Registered", result.Value!.Status);
        Assert.Equal("Online", result.Value.Mode);
    }

    [Fact]
    public async Task RegisterAsync_UnrecognizedMode_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);

        var result = await service.RegisterAsync(member.UserId, @event.Id, "InPerson");

        Assert.False(result.Succeeded);
    }

    /// <summary>Matches spec.md's "A member cannot register twice for the same event" - even under
    /// a different Mode.</summary>
    [Fact]
    public async Task RegisterAsync_Twice_SecondCallFailsEvenUnderADifferentMode()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        await service.RegisterAsync(member.UserId, @event.Id, "Onsite");

        var result = await service.RegisterAsync(member.UserId, @event.Id, "Online");

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
        var member = await SeedMemberForEventTestsAsync(db);
        var first = await service.RegisterAsync(member.UserId, @event.Id, "Onsite");
        await service.CancelRegistrationAsync(member.UserId, first.Value!.Id);

        var result = await service.RegisterAsync(member.UserId, @event.Id, "Onsite");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task CancelRegistrationAsync_NotOwner_Forbidden()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        var otherUserId = Guid.NewGuid();

        var result = await service.CancelRegistrationAsync(otherUserId, registration.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultErrorType.Forbidden, result.ErrorType);
    }

    [Fact]
    public async Task CancelRegistrationAsync_AfterPaymentVerified_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        var entity = await db.EventRegistrations.FindAsync(registration.Id);
        entity!.Status = EventRegistrationStatus.PaymentVerified;
        await db.SaveChangesAsync();

        var result = await service.CancelRegistrationAsync(member.UserId, registration.Id);

        Assert.False(result.Succeeded);
    }
```

Add the `using PSMPE.Portal.Application.Common.Models;` import to `EventServiceTests.cs` for
`ResultErrorType`, if not already present.

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter EventServiceTests`
Expected: FAIL to compile — the new `IEventService` members have no implementation yet.

- [ ] **Step 5: Implement the new members in a second `EventService` partial block**

Create `src/PSMPE.Portal.Application/Events/EventService.Registration.cs` (a second file for the
same `partial class EventService` declared in Task 4 — keeps each concern's file small rather than
growing one file across eight tasks):

```csharp
using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Events.Dtos;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Events;

public partial class EventService
{
    public async Task<Result<EventRegistrationDto>> RegisterAsync(
        Guid userId, Guid eventId, string mode, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<EventMode>(mode, ignoreCase: true, out var parsedMode))
        {
            return Result<EventRegistrationDto>.Failure($"'{mode}' is not a recognized registration mode. Use 'Onsite' or 'Online'.");
        }

        var member = await db.Members.FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (member is null)
        {
            return Result<EventRegistrationDto>.Failure("No member profile found for this account.");
        }

        var @event = await db.Events.Include(e => e.Sessions).FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
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

        var registration = new EventRegistration { EventId = eventId, MemberId = member.Id, Mode = parsedMode };
        db.EventRegistrations.Add(registration);
        await db.SaveChangesAsync(cancellationToken);

        return Result<EventRegistrationDto>.Success(
            ToRegistrationDto(registration, @event, member, sessionsAttended: 0, totalSessions: @event.Sessions.Count));
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

    /// <summary>Shared by every task that returns an EventRegistrationDto (this one, and Tasks
    /// 6–7's attendance/evaluation methods).</summary>
    private static EventRegistrationDto ToRegistrationDto(
        EventRegistration r, Event e, Member m, int sessionsAttended, int totalSessions) =>
        new(r.Id, r.EventId, e.Title, e.StartsAt, r.MemberId, $"{m.FirstName} {m.LastName}", m.MembershipNo,
            r.Mode.ToString(), r.Status.ToString(), sessionsAttended, totalSessions,
            r.EvaluationRating, r.EvaluationComments, r.EvaluationSubmittedAt,
            CpdCredit.For(r, e, sessionsAttended, totalSessions));
}
```

This references `CpdCredit.For`, which doesn't exist yet — that's Task 8. Create a temporary
placeholder now so this compiles, at `src/PSMPE.Portal.Application/Events/CpdCredit.cs`:

```csharp
namespace PSMPE.Portal.Application.Events;

using PSMPE.Portal.Domain.Entities;

/// <summary>Real implementation lands in Task 8 - see CpdCreditTests.cs there. This placeholder
/// only unblocks the build for Tasks 5–7.</summary>
internal static class CpdCredit
{
    public static decimal? For(EventRegistration registration, Event @event, int sessionsAttended, int totalSessions) => null;
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter EventServiceTests`
Expected: PASS (all tests from Step 3 and Task 4). `CreditUnits` is asserted as null nowhere in
this task's own tests, so the `CpdCredit` placeholder returning `null` unconditionally doesn't
break anything yet.

- [ ] **Step 7: Commit**

```bash
git add src/PSMPE.Portal.Application/Events/ tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs
git commit -m "feat: add event registration and cancellation to EventService"
```

---

## 6. Attendance: admin roster reconciliation

**Files:**
- Create: `src/PSMPE.Portal.Application/Events/Dtos/EventAttendanceDto.cs`
- Modify: `src/PSMPE.Portal.Application/Events/IEventService.cs`
- Create: `src/PSMPE.Portal.Application/Events/EventService.Attendance.cs`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs`

There is no member self-check-in anywhere in this design — attendance is recorded exclusively by an
Admin, after the event, reconciling against PSMPE's own PRC sign-in sheet. `RecordAttendanceAsync`
takes the full, authoritative set of sessions each registrant attended on every call (a full
replace, not an incremental add), so an admin correcting a mistake just calls it again with the
corrected set.

- [ ] **Step 1: Create the attendance request DTOs**

```csharp
namespace PSMPE.Portal.Application.Events.Dtos;

/// <summary>One registrant's authoritative set of attended sessions for this call - SessionIds
/// fully replaces whatever EventAttendance rows already exist for RegistrationId, so re-running
/// reconciliation with a corrected set is how a mistake gets fixed (see spec.md's "admin reconciles
/// roster attendance" scenarios).</summary>
public record RegistrantAttendanceRequest(Guid RegistrationId, IReadOnlyList<Guid> SessionIds);

/// <summary>The request body for POST /api/events/{id}/roster/attendance - one call reconciles the
/// whole roster, not just one registrant, since that's how an admin actually works through a
/// printed sign-in sheet.</summary>
public record RecordAttendanceRequest(IReadOnlyList<RegistrantAttendanceRequest> Registrants);
```

- [ ] **Step 2: Add the new member to `IEventService`**

```csharp
    Task<Result> RecordAttendanceAsync(
        Guid eventId, IReadOnlyList<RegistrantAttendanceRequest> registrants, Guid adminUserId, CancellationToken cancellationToken = default);
```

- [ ] **Step 3: Write the failing tests**

Append to `EventServiceTests.cs`:

```csharp
    [Fact]
    public async Task RecordAttendanceAsync_BeforePaymentVerified_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        var sessionId = @event.Sessions.Single().Id;

        var result = await service.RecordAttendanceAsync(
            @event.Id, [new RegistrantAttendanceRequest(registration.Id, [sessionId])], Guid.NewGuid());

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RecordAttendanceAsync_RecordsSessions_MovesRegistrationToAttended()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await MarkPaymentVerifiedAsync(db, registration.Id);
        var sessionId = @event.Sessions.Single().Id;
        var adminUserId = Guid.NewGuid();

        var result = await service.RecordAttendanceAsync(
            @event.Id, [new RegistrantAttendanceRequest(registration.Id, [sessionId])], adminUserId);

        Assert.True(result.Succeeded);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.Attended, updated!.Status);
        var attendance = Assert.Single(db.EventAttendances.Where(a => a.EventRegistrationId == registration.Id));
        Assert.Equal(adminUserId, attendance.RecordedBy);
    }

    /// <summary>Matches spec.md's "A member attends only part of a multi-session event": 3 of 6
    /// sessions produces exactly 3 EventAttendance rows.</summary>
    [Fact]
    public async Task RecordAttendanceAsync_PartialAttendance_RecordsExactlyThatManySessions()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var sixSessions = Enumerable.Range(1, 6)
            .Select(i => new EventSessionRequest(i == 1 ? created.Sessions[0].Id : null, $"Lecture {i}", created.StartsAt.AddHours(i), created.StartsAt.AddHours(i + 1), i))
            .ToList();
        var @event = (await service.UpdateAsync(created.Id, ToUpdateRequest(created) with { Sessions = sixSessions })).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await MarkPaymentVerifiedAsync(db, registration.Id);
        var attendedSessionIds = @event.Sessions.Take(3).Select(s => s.Id).ToList();

        var result = await service.RecordAttendanceAsync(
            @event.Id, [new RegistrantAttendanceRequest(registration.Id, attendedSessionIds)], Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.Equal(3, db.EventAttendances.Count(a => a.EventRegistrationId == registration.Id));
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.Attended, updated!.Status);
    }

    [Fact]
    public async Task RecordAttendanceAsync_SessionFromDifferentEvent_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var otherEvent = (await service.CreateAsync(ValidCreateRequest("Other Event"))).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await MarkPaymentVerifiedAsync(db, registration.Id);
        var otherEventSessionId = otherEvent.Sessions.Single().Id;

        var result = await service.RecordAttendanceAsync(
            @event.Id, [new RegistrantAttendanceRequest(registration.Id, [otherEventSessionId])], Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Empty(db.EventAttendances.Where(a => a.EventRegistrationId == registration.Id));
    }

    [Fact]
    public async Task RecordAttendanceAsync_CalledAgainWithCorrectedSet_ReplacesPreviousRows()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var twoSessions = new List<EventSessionRequest>
        {
            new(created.Sessions[0].Id, "Lecture 1", created.StartsAt, created.StartsAt.AddHours(1), 1),
            new(null, "Lecture 2", created.StartsAt.AddHours(1), created.EndsAt, 2),
        };
        var @event = (await service.UpdateAsync(created.Id, ToUpdateRequest(created) with { Sessions = twoSessions })).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await MarkPaymentVerifiedAsync(db, registration.Id);
        await service.RecordAttendanceAsync(@event.Id, [new RegistrantAttendanceRequest(registration.Id, [@event.Sessions[0].Id])], Guid.NewGuid());

        var result = await service.RecordAttendanceAsync(
            @event.Id, [new RegistrantAttendanceRequest(registration.Id, [@event.Sessions[0].Id, @event.Sessions[1].Id])], Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.Equal(2, db.EventAttendances.Count(a => a.EventRegistrationId == registration.Id));
    }

    private static async Task MarkPaymentVerifiedAsync(TestDbContext db, Guid registrationId)
    {
        var registration = await db.EventRegistrations.FindAsync(registrationId);
        registration!.Status = EventRegistrationStatus.PaymentVerified;
        await db.SaveChangesAsync();
    }
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter EventServiceTests`
Expected: FAIL to compile — `RecordAttendanceAsync` doesn't exist yet.

- [ ] **Step 5: Implement `RecordAttendanceAsync`**

```csharp
using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Events.Dtos;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Events;

public partial class EventService
{
    public async Task<Result> RecordAttendanceAsync(
        Guid eventId, IReadOnlyList<RegistrantAttendanceRequest> registrants, Guid adminUserId,
        CancellationToken cancellationToken = default)
    {
        var registrationIds = registrants.Select(r => r.RegistrationId).ToList();
        var registrations = await db.EventRegistrations
            .Where(r => registrationIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        var validSessionIds = (await db.EventSessions
            .Where(s => s.EventId == eventId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        foreach (var registrant in registrants)
        {
            if (!registrations.TryGetValue(registrant.RegistrationId, out var registration) || registration.EventId != eventId)
            {
                return Result.Failure($"Registration '{registrant.RegistrationId}' does not belong to this event.");
            }

            if (registration.Status is not (EventRegistrationStatus.PaymentVerified or EventRegistrationStatus.Attended or EventRegistrationStatus.EvaluationSubmitted))
            {
                return Result.Failure($"Registration '{registrant.RegistrationId}' needs a verified payment before attendance can be recorded.");
            }

            if (registrant.SessionIds.Any(id => !validSessionIds.Contains(id)))
            {
                return Result.Failure("One or more sessions do not belong to this event.");
            }
        }

        foreach (var registrant in registrants)
        {
            var registration = registrations[registrant.RegistrationId];

            var existing = await db.EventAttendances
                .Where(a => a.EventRegistrationId == registrant.RegistrationId)
                .ToListAsync(cancellationToken);
            db.EventAttendances.RemoveRange(existing);

            foreach (var sessionId in registrant.SessionIds.Distinct())
            {
                db.EventAttendances.Add(new EventAttendance
                {
                    EventRegistrationId = registrant.RegistrationId,
                    EventSessionId = sessionId,
                    RecordedBy = adminUserId,
                    RecordedAt = DateTimeOffset.UtcNow,
                });
            }

            if (registrant.SessionIds.Count > 0 && registration.Status == EventRegistrationStatus.PaymentVerified)
            {
                registration.Status = EventRegistrationStatus.Attended;
                registration.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter EventServiceTests`
Expected: PASS (all tests from Step 3 and prior tasks).

- [ ] **Step 7: Commit**

```bash
git add src/PSMPE.Portal.Application/Events/ tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs
git commit -m "feat: add admin roster attendance reconciliation to EventService"
```

---

## 7. Post-event evaluation

**Files:**
- Create: `src/PSMPE.Portal.Application/Events/Dtos/SubmitEvaluationRequest.cs`
- Modify: `src/PSMPE.Portal.Application/Events/IEventService.cs`
- Create: `src/PSMPE.Portal.Application/Events/EventService.Evaluation.cs`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs`

- [ ] **Step 1: Create `SubmitEvaluationRequest`**

```csharp
namespace PSMPE.Portal.Application.Events.Dtos;

public record SubmitEvaluationRequest(int Rating, string? Comments);
```

- [ ] **Step 2: Add the new member to `IEventService`**

```csharp
    Task<Result> SubmitEvaluationAsync(Guid userId, Guid registrationId, int rating, string? comments, CancellationToken cancellationToken = default);
```

- [ ] **Step 3: Write the failing tests**

Append to `EventServiceTests.cs`:

```csharp
    [Fact]
    public async Task SubmitEvaluationAsync_BeforeAttended_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;

        var result = await service.SubmitEvaluationAsync(member.UserId, registration.Id, rating: 5, comments: "Great");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SubmitEvaluationAsync_AfterAttended_MovesToEvaluationSubmitted()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await MarkAttendedAsync(db, registration.Id);

        var result = await service.SubmitEvaluationAsync(member.UserId, registration.Id, rating: 4, comments: "Good session");

        Assert.True(result.Succeeded);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.EvaluationSubmitted, updated!.Status);
        Assert.NotNull(updated.EvaluationSubmittedAt);
    }

    [Fact]
    public async Task SubmitEvaluationAsync_NotOwner_Forbidden()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await MarkAttendedAsync(db, registration.Id);

        var result = await service.SubmitEvaluationAsync(Guid.NewGuid(), registration.Id, rating: 4, comments: null);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultErrorType.Forbidden, result.ErrorType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task SubmitEvaluationAsync_RatingOutOfRange_Fails(int rating)
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await MarkAttendedAsync(db, registration.Id);

        var result = await service.SubmitEvaluationAsync(member.UserId, registration.Id, rating, comments: null);

        Assert.False(result.Succeeded);
    }

    private static async Task MarkAttendedAsync(TestDbContext db, Guid registrationId)
    {
        var registration = await db.EventRegistrations.FindAsync(registrationId);
        registration!.Status = EventRegistrationStatus.Attended;
        await db.SaveChangesAsync();
    }
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter EventServiceTests`
Expected: FAIL to compile — `SubmitEvaluationAsync` doesn't exist yet.

- [ ] **Step 5: Implement `SubmitEvaluationAsync`**

```csharp
using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Events;

public partial class EventService
{
    public async Task<Result> SubmitEvaluationAsync(
        Guid userId, Guid registrationId, int rating, string? comments, CancellationToken cancellationToken = default)
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
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter EventServiceTests`
Expected: PASS (all tests from Step 3 and prior tasks).

- [ ] **Step 7: Commit**

```bash
git add src/PSMPE.Portal.Application/Events/ tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs
git commit -m "feat: add post-event evaluation submission to EventService"
```

---

## 8. CPD credit computation and "My CPD" query

**Files:**
- Modify: `src/PSMPE.Portal.Application/Events/CpdCredit.cs`
- Create: `src/PSMPE.Portal.Application/Events/Dtos/MyCpdSummaryDto.cs`
- Modify: `src/PSMPE.Portal.Application/Events/IEventService.cs`
- Create: `src/PSMPE.Portal.Application/Events/EventService.Cpd.cs`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Events/CpdCreditTests.cs`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs`

- [ ] **Step 1: Write the failing tests for `CpdCredit.For`**

```csharp
using PSMPE.Portal.Application.Events;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Events;

public class CpdCreditTests
{
    private static EventRegistration Registration(EventMode mode, EventRegistrationStatus status = EventRegistrationStatus.EvaluationSubmitted) =>
        new() { Mode = mode, Status = status };

    [Fact]
    public void For_NotEvaluationSubmitted_ReturnsNull()
    {
        var registration = Registration(EventMode.Onsite, EventRegistrationStatus.Attended);
        var @event = new Event { CpdUnitsOnsite = 8m };

        var credit = CpdCredit.For(registration, @event, sessionsAttended: 6, totalSessions: 6);

        Assert.Null(credit);
    }

    [Fact]
    public void For_ApplicableModalityUnitsStillNull_ReturnsNull()
    {
        var registration = Registration(EventMode.Online);
        var @event = new Event { CpdUnitsOnsite = 8m, CpdUnitsOnline = null };

        var credit = CpdCredit.For(registration, @event, sessionsAttended: 6, totalSessions: 6);

        Assert.Null(credit);
    }

    /// <summary>Matches spec.md's "Partial attendance earns prorated credit": 3 of 6 sessions on an
    /// 8-unit event earns 4 (8 x 3/6).</summary>
    [Fact]
    public void For_PartialAttendance_ReturnsProratedValue()
    {
        var registration = Registration(EventMode.Onsite);
        var @event = new Event { CpdUnitsOnsite = 8m };

        var credit = CpdCredit.For(registration, @event, sessionsAttended: 3, totalSessions: 6);

        Assert.Equal(4m, credit);
    }

    /// <summary>Matches spec.md's "Onsite and Online registrations on the same event earn different
    /// credit".</summary>
    [Theory]
    [InlineData(EventMode.Onsite, 8)]
    [InlineData(EventMode.Online, 4)]
    public void For_FullAttendance_UsesUnitsForTheRegistrationsOwnMode(EventMode mode, decimal expected)
    {
        var registration = Registration(mode);
        var @event = new Event { CpdUnitsOnsite = 8m, CpdUnitsOnline = 4m };

        var credit = CpdCredit.For(registration, @event, sessionsAttended: 6, totalSessions: 6);

        Assert.Equal(expected, credit);
    }

    [Fact]
    public void For_ZeroTotalSessions_ReturnsNull()
    {
        var registration = Registration(EventMode.Onsite);
        var @event = new Event { CpdUnitsOnsite = 8m };

        var credit = CpdCredit.For(registration, @event, sessionsAttended: 0, totalSessions: 0);

        Assert.Null(credit);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter CpdCreditTests`
Expected: FAIL — the Task 5 placeholder always returns `null`, so
`For_PartialAttendance_ReturnsProratedValue` and `For_FullAttendance_UsesUnitsForTheRegistrationsOwnMode`
fail their `Assert.Equal`.

- [ ] **Step 3: Replace the `CpdCredit` placeholder with the real implementation**

```csharp
namespace PSMPE.Portal.Application.Events;

using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

/// <summary>
/// CPD credit is computed here, never stored on EventRegistration - see the design note at the top
/// of tasks.md and add-events-cpd-tracker/proposal.md. A registration only counts once it has
/// completed the full loop (evaluation submitted) AND the applicable modality's unit count has been
/// set, and the value is prorated by how many of the event's sessions were actually attended.
/// </summary>
internal static class CpdCredit
{
    public static decimal? For(EventRegistration registration, Event @event, int sessionsAttended, int totalSessions)
    {
        if (registration.Status != EventRegistrationStatus.EvaluationSubmitted || totalSessions <= 0)
        {
            return null;
        }

        var unitsForMode = registration.Mode == EventMode.Onsite ? @event.CpdUnitsOnsite : @event.CpdUnitsOnline;
        return unitsForMode is null ? null : unitsForMode.Value * sessionsAttended / totalSessions;
    }
}
```

- [ ] **Step 4: Run the `CpdCredit` tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter CpdCreditTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Create `MyCpdSummaryDto`**

```csharp
namespace PSMPE.Portal.Application.Events.Dtos;

public record MyCpdRegistrationDto(
    Guid RegistrationId,
    Guid EventId,
    string EventTitle,
    DateTimeOffset EventStartsAt,
    string Mode,
    string Status,
    int SessionsAttended,
    int TotalSessions,
    decimal? CreditUnits);

public record MyCpdSummaryDto(decimal TotalCreditUnits, IReadOnlyList<MyCpdRegistrationDto> Registrations);
```

- [ ] **Step 6: Add the new member to `IEventService`**

```csharp
    Task<MyCpdSummaryDto> GetMyCpdAsync(Guid userId, CancellationToken cancellationToken = default);
```

- [ ] **Step 7: Write the failing tests for `GetMyCpdAsync`**

Append to `EventServiceTests.cs`:

```csharp
    /// <summary>Matches spec.md's "A member's CPD total reflects only completed, credited
    /// registrations".</summary>
    [Fact]
    public async Task GetMyCpdAsync_SumsOnlyEvaluationSubmittedRegistrationsWithNonNullUnits()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var member = await SeedMemberForEventTestsAsync(db);

        var creditedEvent = (await service.CreateAsync(ValidCreateRequest("Credited Event"))).Value!;
        await service.UpdateAsync(creditedEvent.Id, ToUpdateRequest(creditedEvent) with { CpdUnitsOnsite = 8m });
        var creditedRegistration = (await service.RegisterAsync(member.UserId, creditedEvent.Id, "Onsite")).Value!;
        await MarkPaymentVerifiedAsync(db, creditedRegistration.Id);
        await service.RecordAttendanceAsync(
            creditedEvent.Id, [new RegistrantAttendanceRequest(creditedRegistration.Id, [creditedEvent.Sessions.Single().Id])], Guid.NewGuid());
        await service.SubmitEvaluationAsync(member.UserId, creditedRegistration.Id, rating: 5, comments: null);

        var tbdEvent = (await service.CreateAsync(ValidCreateRequest("TBD Units Event"))).Value!;
        var tbdRegistration = (await service.RegisterAsync(member.UserId, tbdEvent.Id, "Onsite")).Value!;
        await MarkPaymentVerifiedAsync(db, tbdRegistration.Id);
        await service.RecordAttendanceAsync(
            tbdEvent.Id, [new RegistrantAttendanceRequest(tbdRegistration.Id, [tbdEvent.Sessions.Single().Id])], Guid.NewGuid());
        await service.SubmitEvaluationAsync(member.UserId, tbdRegistration.Id, rating: 5, comments: null);

        var notYetEvaluatedEvent = (await service.CreateAsync(ValidCreateRequest("Not Yet Evaluated Event"))).Value!;
        await service.UpdateAsync(notYetEvaluatedEvent.Id, ToUpdateRequest(notYetEvaluatedEvent) with { CpdUnitsOnsite = 6m });
        await service.RegisterAsync(member.UserId, notYetEvaluatedEvent.Id, "Onsite");

        var summary = await service.GetMyCpdAsync(member.UserId);

        Assert.Equal(8m, summary.TotalCreditUnits);
        Assert.Equal(3, summary.Registrations.Count);
    }

    [Fact]
    public async Task GetMyCpdAsync_NoMemberProfile_ReturnsEmptySummary()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);

        var summary = await service.GetMyCpdAsync(Guid.NewGuid());

        Assert.Equal(0m, summary.TotalCreditUnits);
        Assert.Empty(summary.Registrations);
    }
```

- [ ] **Step 8: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter EventServiceTests`
Expected: FAIL to compile — `GetMyCpdAsync` doesn't exist yet.

- [ ] **Step 9: Implement `GetMyCpdAsync`**

```csharp
using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Events.Dtos;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Events;

public partial class EventService
{
    public async Task<MyCpdSummaryDto> GetMyCpdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (member is null)
        {
            return new MyCpdSummaryDto(0m, []);
        }

        var registrations = await db.EventRegistrations
            .Include(r => r.Event).ThenInclude(e => e.Sessions)
            .Where(r => r.MemberId == member.Id && r.Status != EventRegistrationStatus.Cancelled)
            .ToListAsync(cancellationToken);
        var registrationIds = registrations.Select(r => r.Id).ToList();

        var attendanceCounts = await db.EventAttendances
            .Where(a => registrationIds.Contains(a.EventRegistrationId))
            .GroupBy(a => a.EventRegistrationId)
            .Select(g => new { RegistrationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.RegistrationId, g => g.Count, cancellationToken);

        var items = registrations.Select(r =>
        {
            var sessionsAttended = attendanceCounts.GetValueOrDefault(r.Id);
            var totalSessions = r.Event.Sessions.Count;
            var credit = CpdCredit.For(r, r.Event, sessionsAttended, totalSessions);
            return new MyCpdRegistrationDto(
                r.Id, r.EventId, r.Event.Title, r.Event.StartsAt, r.Mode.ToString(), r.Status.ToString(),
                sessionsAttended, totalSessions, credit);
        }).ToList();

        var total = items.Sum(i => i.CreditUnits ?? 0m);
        return new MyCpdSummaryDto(total, items);
    }
}
```

- [ ] **Step 10: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter EventServiceTests`
Expected: PASS (all tests from Step 7 and prior tasks).

- [ ] **Step 11: Commit**

```bash
git add src/PSMPE.Portal.Application/Events/ tests/PSMPE.Portal.Application.UnitTests/Events/CpdCreditTests.cs \
  tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs
git commit -m "feat: implement prorated CPD credit computation and My CPD summary"
```

---

## 9. Payment integration

**Files:**
- Create: `src/PSMPE.Portal.Application/Payments/EventPaymentVerification.cs`
- Modify: `src/PSMPE.Portal.Application/Payments/IPaymentService.cs`
- Modify: `src/PSMPE.Portal.Application/Payments/PaymentService.cs`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Payments/PaymentServiceTests.cs`

`PaymentsController` itself needs **no changes** — `POST /api/payments/{id}/verify`,
`POST /api/payments/{id}/reject`, and `POST /api/payments/{id}/proof` already work off a bare
`Payment.Id` with no assumption about `Kind`, so an event registration's payment rides the exact
same endpoints a membership payment uses. Only `PaymentService`'s bodies grow a branch. The two
genuinely new payment actions (member submits proof for an event registration; admin records a cash
payment) are new `PaymentService` methods, called from the new `EventsController` in Task 12.

- [ ] **Step 1: Write the failing tests for `VerifyAsync`/`RejectAsync` on an event-registration payment**

Append to `PaymentServiceTests.cs`:

```csharp
    private static async Task<(Member Member, EventRegistration Registration)> SeedEventRegistrationAsync(
        TestDbContext db, EventRegistrationStatus status = EventRegistrationStatus.Registered)
    {
        var user = new ApplicationUser { UserName = $"{Guid.NewGuid()}@example.com", Email = $"{Guid.NewGuid()}@example.com" };
        db.Add(user);
        var member = new Member { UserId = user.Id, User = user, FirstName = "Ana", LastName = "Reyes", Chapter = Chapters.Ncr, MemberType = MemberTypes.Regular };
        db.Members.Add(member);

        var @event = new Event { Title = "Seminar", StartsAt = DateTimeOffset.UtcNow.AddDays(5), EndsAt = DateTimeOffset.UtcNow.AddDays(5).AddHours(4), Fee = 500m };
        db.Events.Add(@event);

        var registration = new EventRegistration { EventId = @event.Id, Event = @event, MemberId = member.Id, Member = member, Mode = EventMode.Onsite, Status = status };
        db.EventRegistrations.Add(registration);
        await db.SaveChangesAsync();
        return (member, registration);
    }

    [Fact]
    public async Task SubmitForEventRegistrationAsync_Valid_CreatesPaymentAndMovesToPaymentSubmitted()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (member, registration) = await SeedEventRegistrationAsync(db);

        var result = await service.SubmitForEventRegistrationAsync(
            member.UserId, registration.Id, new SubmitPaymentRequest(500m, "REF-1", DateOnly.FromDateTime(DateTime.UtcNow)));

        Assert.True(result.Succeeded);
        Assert.Equal(PaymentKind.EventRegistration, result.Value!.Kind);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.PaymentSubmitted, updated!.Status);
    }

    [Fact]
    public async Task SubmitForEventRegistrationAsync_SecondSubmissionWhilePending_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (member, registration) = await SeedEventRegistrationAsync(db);
        var request = new SubmitPaymentRequest(500m, "REF-1", DateOnly.FromDateTime(DateTime.UtcNow));
        await service.SubmitForEventRegistrationAsync(member.UserId, registration.Id, request);

        var result = await service.SubmitForEventRegistrationAsync(member.UserId, registration.Id, request);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
    }

    /// <summary>Matches spec.md's "Verifying an event payment advances the registration".</summary>
    [Fact]
    public async Task VerifyAsync_EventRegistrationPayment_MovesRegistrationToPaymentVerified()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (member, registration) = await SeedEventRegistrationAsync(db, EventRegistrationStatus.PaymentSubmitted);
        var payment = new Payment
        {
            MemberId = member.Id, Kind = PaymentKind.EventRegistration, EventRegistrationId = registration.Id,
            Amount = 500m, PaidOn = DateOnly.FromDateTime(DateTime.UtcNow), ProofStorageKey = "proof/key.jpg",
            Status = PaymentStatus.Submitted,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var result = await service.VerifyAsync(payment.Id, Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.Equal(PaymentStatus.Verified, payment.Status);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.PaymentVerified, updated!.Status);
    }

    /// <summary>Matches spec.md's "A rejected event payment can be resubmitted".</summary>
    [Fact]
    public async Task RejectAsync_EventRegistrationPayment_SetsRegistrationRejectedAndAllowsResubmission()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (member, registration) = await SeedEventRegistrationAsync(db, EventRegistrationStatus.PaymentSubmitted);
        var payment = new Payment
        {
            MemberId = member.Id, Kind = PaymentKind.EventRegistration, EventRegistrationId = registration.Id,
            Amount = 500m, PaidOn = DateOnly.FromDateTime(DateTime.UtcNow), ProofStorageKey = "proof/key.jpg",
            Status = PaymentStatus.Submitted,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var rejectResult = await service.RejectAsync(payment.Id, "Amount doesn't match the fee.", Guid.NewGuid());

        Assert.True(rejectResult.Succeeded);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.Rejected, updated!.Status);

        var resubmit = await service.SubmitForEventRegistrationAsync(
            member.UserId, registration.Id, new SubmitPaymentRequest(500m, "REF-2", DateOnly.FromDateTime(DateTime.UtcNow)));
        Assert.True(resubmit.Succeeded);
    }

    /// <summary>Matches spec.md's "An admin records a cash payment".</summary>
    [Fact]
    public async Task RecordEventCashPaymentAsync_Valid_CreatesVerifiedPaymentAndMovesRegistration()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (_, registration) = await SeedEventRegistrationAsync(db);
        var adminUserId = Guid.NewGuid();

        var result = await service.RecordEventCashPaymentAsync(registration.Id, 500m, adminUserId);

        Assert.True(result.Succeeded);
        Assert.Equal(PaymentStatus.Verified, result.Value!.Status);
        Assert.False(result.Value.HasProof);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.PaymentVerified, updated!.Status);
    }

    /// <summary>Matches spec.md's "A cash payment cannot be recorded over an existing payment".</summary>
    [Fact]
    public async Task RecordEventCashPaymentAsync_RegistrationAlreadyHasSubmittedPayment_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (member, registration) = await SeedEventRegistrationAsync(db);
        await service.SubmitForEventRegistrationAsync(
            member.UserId, registration.Id, new SubmitPaymentRequest(500m, "REF-1", DateOnly.FromDateTime(DateTime.UtcNow)));

        var result = await service.RecordEventCashPaymentAsync(registration.Id, 500m, Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task RecordEventCashPaymentAsync_AfterEarlierRejection_Succeeds()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new PaymentService(db);
        var (member, registration) = await SeedEventRegistrationAsync(db);
        var submitted = await service.SubmitForEventRegistrationAsync(
            member.UserId, registration.Id, new SubmitPaymentRequest(500m, "REF-1", DateOnly.FromDateTime(DateTime.UtcNow)));
        await service.RejectAsync(submitted.Value!.Id, "Wrong amount.", Guid.NewGuid());

        var result = await service.RecordEventCashPaymentAsync(registration.Id, 500m, Guid.NewGuid());

        Assert.True(result.Succeeded);
    }
```

Add `using PSMPE.Portal.Domain.Entities;` (for `EventRegistration`, `Event`) and
`using PSMPE.Portal.Application.Events.Dtos;` (for `SubmitPaymentRequest` — no, that one is already
`PSMPE.Portal.Application.Payments.Dtos`, already imported) to `PaymentServiceTests.cs` if not
already present; `EventMode`/`EventRegistrationStatus` come from the existing
`PSMPE.Portal.Domain.Enums` import.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter PaymentServiceTests`
Expected: FAIL to compile — `SubmitForEventRegistrationAsync`/`RecordEventCashPaymentAsync` don't
exist yet, and `VerifyAsync`/`RejectAsync` don't yet touch `EventRegistration.Status`.

- [ ] **Step 3: Create `EventPaymentVerification`**

```csharp
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Payments;

/// <summary>
/// The effect of accepting an event-registration payment, in one place - the EventRegistration
/// counterpart to PaymentVerification.Apply (which is membership-specific: it dereferences
/// Member.ApprovedAt and computes RenewalDueDate, neither of which applies here). Two callers apply
/// it: PaymentService.VerifyAsync (a member's proof was accepted) and
/// PaymentService.RecordEventCashPaymentAsync (an admin recorded cash on the spot) - see
/// add-events-cpd-tracker/proposal.md.
/// </summary>
internal static class EventPaymentVerification
{
    public static void Apply(Payment payment, EventRegistration registration, Guid decidedByUserId)
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

- [ ] **Step 4: Add the two new members to `IPaymentService`**

In `src/PSMPE.Portal.Application/Payments/IPaymentService.cs`, add:

```csharp
    /// <summary>Self-service submission of a proof-of-payment for an event registration - the
    /// EventRegistration counterpart to SubmitAsync. Kind is always EventRegistration, decided by
    /// the caller passing a registrationId rather than trusted from the request body.</summary>
    Task<Result<PaymentDto>> SubmitForEventRegistrationAsync(
        Guid userId, Guid registrationId, SubmitPaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Creates and immediately verifies a Payment with no proof file, for an on-site cash
    /// payer - reaches the same PaymentVerified state as the proof-upload path in one call. Refused
    /// if the registration already has a Submitted or Verified Payment.</summary>
    Task<Result<PaymentDto>> RecordEventCashPaymentAsync(
        Guid registrationId, decimal amount, Guid decidedByUserId, CancellationToken cancellationToken = default);
```

- [ ] **Step 5: Extend `VerifyAsync` and `RejectAsync`, and add the two new methods**

In `src/PSMPE.Portal.Application/Payments/PaymentService.cs`, replace the body of `VerifyAsync`:

```csharp
    public async Task<Result> VerifyAsync(Guid paymentId, Guid decidedByUserId, CancellationToken cancellationToken = default)
    {
        var payment = await db.Payments.Include(p => p.Member)
            .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null)
        {
            return Result.NotFound($"Payment '{paymentId}' was not found.");
        }

        if (payment.Status == PaymentStatus.Verified)
        {
            // Idempotent, same as before - a repeat call must not re-run either side effect.
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
            var registration = payment.EventRegistrationId is null
                ? null
                : await db.EventRegistrations.FirstOrDefaultAsync(r => r.Id == payment.EventRegistrationId, cancellationToken);
            if (registration is null)
            {
                return Result.Failure("The event registration for this payment no longer exists.");
            }
            if (registration.Status != EventRegistrationStatus.PaymentSubmitted)
            {
                return Result.Failure("This registration isn't awaiting payment verification.");
            }

            EventPaymentVerification.Apply(payment, registration, decidedByUserId);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        var member = payment.Member;

        // Payment can't admit someone. Approval is a separate decision that gates on RMP
        // verification (see MemberService.ApproveAsync); paying doesn't bypass it.
        if (member.ApprovedAt is null)
        {
            return Result.Failure("This member's application hasn't been approved yet, so their payment can't activate a membership.");
        }

        PaymentVerification.Apply(payment, member, decidedByUserId);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
```

Replace the body of `RejectAsync`:

```csharp
    public async Task<Result> RejectAsync(Guid paymentId, string reason, Guid decidedByUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure("A reason is required to reject a payment.");
        }

        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null)
        {
            return Result.NotFound($"Payment '{paymentId}' was not found.");
        }

        if (payment.Status == PaymentStatus.Verified)
        {
            // Reversing a verification would have to un-advance a due date (or un-attend a
            // registration) - deliberately not a thing this endpoint does.
            return Result.Failure("This payment was already verified and can't be rejected.");
        }

        if (payment.Kind == PaymentKind.EventRegistration && payment.EventRegistrationId is not null)
        {
            var registration = await db.EventRegistrations.FirstOrDefaultAsync(r => r.Id == payment.EventRegistrationId, cancellationToken);
            if (registration is not null)
            {
                registration.Status = EventRegistrationStatus.Rejected;
                registration.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        payment.Status = PaymentStatus.Rejected;
        payment.RejectedReason = reason.Trim();
        payment.DecidedByUserId = decidedByUserId;
        payment.DecidedAt = DateTimeOffset.UtcNow;
        payment.UpdatedAt = DateTimeOffset.UtcNow;

        // Member Status and RenewalDueDate are deliberately untouched for a NewMembership/Renewal
        // rejection - a rejected renewal leaves the member exactly where they were, still owing.
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
```

Add the two new methods (anywhere in the class, e.g. after `RejectAsync`):

```csharp
    public async Task<Result<PaymentDto>> SubmitForEventRegistrationAsync(
        Guid userId, Guid registrationId, SubmitPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var registration = await db.EventRegistrations.Include(r => r.Member)
            .FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken);
        if (registration is null)
        {
            return Result<PaymentDto>.NotFound($"Registration '{registrationId}' was not found.");
        }
        if (registration.Member.UserId != userId)
        {
            return Result<PaymentDto>.Forbidden("This isn't your registration.");
        }
        if (registration.Status is not (EventRegistrationStatus.Registered or EventRegistrationStatus.Rejected))
        {
            return Result<PaymentDto>.Failure("This registration isn't awaiting payment.");
        }

        if (request.Amount <= 0)
        {
            return Result<PaymentDto>.Failure("Amount must be greater than zero.");
        }
        if (request.PaidOn > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return Result<PaymentDto>.Failure("Payment date can't be in the future.");
        }
        if (request.ReferenceNo?.Length > 64)
        {
            return Result<PaymentDto>.Failure("Reference number must be 64 characters or fewer.");
        }

        var hasPending = await db.Payments.AnyAsync(
            p => p.EventRegistrationId == registrationId && p.Status == PaymentStatus.Submitted, cancellationToken);
        if (hasPending)
        {
            return Result<PaymentDto>.Conflict("You already have a payment awaiting verification for this registration.");
        }

        var payment = new Payment
        {
            MemberId = registration.MemberId,
            Member = registration.Member,
            Kind = PaymentKind.EventRegistration,
            EventRegistrationId = registration.Id,
            Amount = request.Amount,
            ReferenceNo = request.ReferenceNo?.Trim(),
            PaidOn = request.PaidOn,
            Status = PaymentStatus.Submitted,
        };
        db.Payments.Add(payment);

        registration.Status = EventRegistrationStatus.PaymentSubmitted;
        registration.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Result<PaymentDto>.Success(ToDto(payment));
    }

    public async Task<Result<PaymentDto>> RecordEventCashPaymentAsync(
        Guid registrationId, decimal amount, Guid decidedByUserId, CancellationToken cancellationToken = default)
    {
        var registration = await db.EventRegistrations.Include(r => r.Member)
            .FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken);
        if (registration is null)
        {
            return Result<PaymentDto>.NotFound($"Registration '{registrationId}' was not found.");
        }

        if (amount <= 0)
        {
            return Result<PaymentDto>.Failure("Amount must be greater than zero.");
        }

        // "Exactly one Payment, regardless of path" - a Rejected payment doesn't count, same as
        // SubmitForEventRegistrationAsync's own pending check, so a cash payment can still cover a
        // registration whose earlier proof submission was rejected.
        var hasActivePayment = await db.Payments.AnyAsync(
            p => p.EventRegistrationId == registrationId && p.Status != PaymentStatus.Rejected, cancellationToken);
        if (hasActivePayment)
        {
            return Result<PaymentDto>.Conflict("This registration already has a submitted or verified payment.");
        }

        var payment = new Payment
        {
            MemberId = registration.MemberId,
            Member = registration.Member,
            Kind = PaymentKind.EventRegistration,
            EventRegistrationId = registration.Id,
            Amount = amount,
            PaidOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = PaymentStatus.Submitted,
        };
        db.Payments.Add(payment);

        EventPaymentVerification.Apply(payment, registration, decidedByUserId);

        await db.SaveChangesAsync(cancellationToken);
        return Result<PaymentDto>.Success(ToDto(payment));
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter PaymentServiceTests`
Expected: PASS (all tests from Step 1 and the pre-existing membership-payment tests, which must
still pass unmodified — `VerifyAsync`/`RejectAsync`'s `NewMembership`/`Renewal` behavior is
untouched by the new `Kind == EventRegistration` branch).

- [ ] **Step 7: Commit**

```bash
git add src/PSMPE.Portal.Application/Payments/ tests/PSMPE.Portal.Application.UnitTests/Payments/PaymentServiceTests.cs
git commit -m "feat: integrate event registration payments into PaymentService"
```

---

## 10. Roster query

**Files:**
- Create: `src/PSMPE.Portal.Application/Events/Dtos/EventRosterDto.cs`
- Modify: `src/PSMPE.Portal.Application/Events/IEventService.cs`
- Create: `src/PSMPE.Portal.Application/Events/EventService.Roster.cs`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs`

- [ ] **Step 1: Create `EventRosterDto`**

```csharp
namespace PSMPE.Portal.Application.Events.Dtos;

public record EventRosterEntryDto(
    Guid RegistrationId,
    Guid MemberId,
    string MemberName,
    string? MembershipNo,
    string Mode,
    string Status,
    IReadOnlyList<Guid> AttendedSessionIds,
    int TotalSessions,
    Guid? PaymentId,
    string? PaymentStatus,
    /// <summary>True for a cash payment (no proof file ever attached), false for a proof-upload
    /// payment, null if there's no Payment on this registration yet. Derived from
    /// Payment.ProofStorageKey being null - a proof payment always has one attached before it can
    /// reach Verified (see PaymentService.VerifyAsync), so there's no need for a separate stored
    /// flag.</summary>
    bool? PaymentIsCash,
    string? PaymentRejectedReason,
    int? EvaluationRating,
    DateTimeOffset? EvaluationSubmittedAt,
    decimal? CreditUnits);

public record EventRosterDto(
    Guid EventId,
    string EventTitle,
    IReadOnlyList<EventSessionDto> Sessions,
    IReadOnlyList<EventRosterEntryDto> Registrants);
```

- [ ] **Step 2: Add the new member to `IEventService`**

```csharp
    Task<Result<EventRosterDto>> GetRosterAsync(Guid eventId, CancellationToken cancellationToken = default);
```

- [ ] **Step 3: Write the failing tests**

Append to `EventServiceTests.cs`:

```csharp
    [Fact]
    public async Task GetRosterAsync_UnknownEvent_NotFound()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);

        var result = await service.GetRosterAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task GetRosterAsync_ReturnsPerSessionAttendancePaymentAndEvaluationState()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        var paymentService = new PaymentService(db);
        var submitted = await paymentService.SubmitForEventRegistrationAsync(
            member.UserId, registration.Id, new SubmitPaymentRequest(500m, "REF-1", DateOnly.FromDateTime(DateTime.UtcNow)));
        db.Payments.First(p => p.Id == submitted.Value!.Id).ProofStorageKey = "proof/key.jpg";
        await db.SaveChangesAsync();
        await paymentService.VerifyAsync(submitted.Value!.Id, Guid.NewGuid());
        var sessionId = @event.Sessions.Single().Id;
        await service.RecordAttendanceAsync(@event.Id, [new RegistrantAttendanceRequest(registration.Id, [sessionId])], Guid.NewGuid());
        await service.SubmitEvaluationAsync(member.UserId, registration.Id, rating: 5, comments: "Excellent");

        var result = await service.GetRosterAsync(@event.Id);

        Assert.True(result.Succeeded);
        var entry = Assert.Single(result.Value!.Registrants);
        Assert.Equal("EvaluationSubmitted", entry.Status);
        Assert.Equal([sessionId], entry.AttendedSessionIds);
        Assert.Equal(1, entry.TotalSessions);
        Assert.Equal("Verified", entry.PaymentStatus);
        Assert.False(entry.PaymentIsCash);
        Assert.Equal(5, entry.EvaluationRating);
    }

    [Fact]
    public async Task GetRosterAsync_CashPayment_ReportsPaymentIsCashTrue()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        var paymentService = new PaymentService(db);
        await paymentService.RecordEventCashPaymentAsync(registration.Id, 500m, Guid.NewGuid());

        var result = await service.GetRosterAsync(@event.Id);

        var entry = Assert.Single(result.Value!.Registrants);
        Assert.True(entry.PaymentIsCash);
    }

    [Fact]
    public async Task GetRosterAsync_ExcludesCancelledRegistrations()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await service.CancelRegistrationAsync(member.UserId, registration.Id);

        var result = await service.GetRosterAsync(@event.Id);

        Assert.Empty(result.Value!.Registrants);
    }
```

Add `using PSMPE.Portal.Application.Payments;` and `using PSMPE.Portal.Application.Payments.Dtos;`
to `EventServiceTests.cs` for `PaymentService`/`SubmitPaymentRequest`.

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter EventServiceTests`
Expected: FAIL to compile — `GetRosterAsync` doesn't exist yet.

- [ ] **Step 5: Implement `GetRosterAsync`**

```csharp
using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Events.Dtos;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Events;

public partial class EventService
{
    public async Task<Result<EventRosterDto>> GetRosterAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var @event = await db.Events.Include(e => e.Sessions).FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
        if (@event is null)
        {
            return Result<EventRosterDto>.NotFound($"Event '{eventId}' was not found.");
        }

        var registrations = await db.EventRegistrations.Include(r => r.Member)
            .Where(r => r.EventId == eventId && r.Status != EventRegistrationStatus.Cancelled)
            .ToListAsync(cancellationToken);
        var registrationIds = registrations.Select(r => r.Id).ToList();

        var attendanceByRegistration = (await db.EventAttendances
            .Where(a => registrationIds.Contains(a.EventRegistrationId))
            .ToListAsync(cancellationToken))
            .GroupBy(a => a.EventRegistrationId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(a => a.EventSessionId).ToList());

        var paymentByRegistration = (await db.Payments
            .Where(p => p.EventRegistrationId != null && registrationIds.Contains(p.EventRegistrationId!.Value))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken))
            .GroupBy(p => p.EventRegistrationId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var totalSessions = @event.Sessions.Count;
        var entries = registrations.Select(r =>
        {
            var attendedSessionIds = attendanceByRegistration.GetValueOrDefault(r.Id, []);
            paymentByRegistration.TryGetValue(r.Id, out var payment);
            return new EventRosterEntryDto(
                r.Id, r.MemberId, $"{r.Member.FirstName} {r.Member.LastName}", r.Member.MembershipNo,
                r.Mode.ToString(), r.Status.ToString(), attendedSessionIds, totalSessions,
                payment?.Id, payment?.Status.ToString(), payment is null ? null : payment.ProofStorageKey is null,
                payment?.RejectedReason, r.EvaluationRating, r.EvaluationSubmittedAt,
                CpdCredit.For(r, @event, attendedSessionIds.Count, totalSessions));
        }).ToList();

        var sessions = @event.Sessions.OrderBy(s => s.Order)
            .Select(s => new EventSessionDto(s.Id, s.Title, s.StartsAt, s.EndsAt, s.Order))
            .ToList();

        return Result<EventRosterDto>.Success(new EventRosterDto(@event.Id, @event.Title, sessions, entries));
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter EventServiceTests`
Expected: PASS (all tests from Step 3 and prior tasks).

- [ ] **Step 7: Commit**

```bash
git add src/PSMPE.Portal.Application/Events/ tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs
git commit -m "feat: add per-session roster query to EventService"
```

---

## 11. Certificate PDF generation

**Files:**
- Modify: `src/PSMPE.Portal.Application/PSMPE.Portal.Application.csproj`
- Modify: `src/PSMPE.Portal.WebAPI/Program.cs`
- Create: `src/PSMPE.Portal.Application/Events/Dtos/CertificateDataDto.cs`
- Modify: `src/PSMPE.Portal.Application/Events/IEventService.cs`
- Create: `src/PSMPE.Portal.Application/Events/EventService.Certificate.cs`
- Create: `src/PSMPE.Portal.Application/Events/CertificatePdfGenerator.cs`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Events/CertificatePdfGeneratorTests.cs`

`GetCertificateDataAsync` (this task) is the Application-layer query that decides *whether* a
certificate is available and assembles what goes on it — pure data, fully unit-testable against the
InMemory provider. `CertificatePdfGenerator.Generate` (also this task) is the QuestPDF rendering
step; it has no branching logic worth a full TDD cycle, so it gets one smoke test confirming it
produces non-empty PDF bytes rather than a red/green cycle per visual detail.

- [ ] **Step 1: Add the QuestPDF package reference**

```bash
dotnet add src/PSMPE.Portal.Application/PSMPE.Portal.Application.csproj package QuestPDF --version 2024.12.3
```

Expected: `PSMPE.Portal.Application.csproj` gains a `<PackageReference Include="QuestPDF" Version="2024.12.3" />`
entry. (If that exact patch version is unavailable from NuGet by the time this task runs, install
the latest `2024.x` release instead — QuestPDF's public API used below has been stable across that
whole line.) QuestPDF's rendering pipeline depends on SkiaSharp, which this project already
references for other image handling, so no further native-asset packages are needed.

- [ ] **Step 2: Set the QuestPDF license at startup**

In `src/PSMPE.Portal.WebAPI/Program.cs`, add near the top, right after `var builder = WebApplication.CreateBuilder(args);`:

```csharp
// Required by QuestPDF as of its Community-license versions - without this, every PDF generation
// call throws at runtime. Community is free for PSMPE's use (a single small organization, not a
// >$1M-revenue company reselling the software) - see QuestPDF's license terms if that ever changes.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
```

Add `using QuestPDF.Infrastructure;` is not required since the type is fully qualified above; either
form compiles.

- [ ] **Step 3: Create `CertificateDataDto`**

```csharp
namespace PSMPE.Portal.Application.Events.Dtos;

public record CertificateDataDto(
    string MemberName,
    string EventTitle,
    DateTimeOffset EventStartsAt,
    DateTimeOffset EventEndsAt,
    string Mode,
    IReadOnlyList<string> AttendedSessionTitles,
    decimal CreditUnits);
```

- [ ] **Step 4: Add the new member to `IEventService`**

```csharp
    /// <summary>isAdmin bypasses the ownership check - an Admin can pull any registration's
    /// certificate data, a member only their own.</summary>
    Task<Result<CertificateDataDto>> GetCertificateDataAsync(
        Guid userId, Guid registrationId, bool isAdmin, CancellationToken cancellationToken = default);
```

- [ ] **Step 5: Write the failing tests for `GetCertificateDataAsync`**

Append to `EventServiceTests.cs`:

```csharp
    private async Task<(EventDto Event, EventRegistrationDto Registration, Member Member)> SeedCreditedRegistrationAsync(
        TestDbContext db, EventService service, decimal onsiteUnits = 8m)
    {
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var @event = (await service.UpdateAsync(created.Id, ToUpdateRequest(created) with { CpdUnitsOnsite = onsiteUnits })).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await MarkPaymentVerifiedAsync(db, registration.Id);
        await service.RecordAttendanceAsync(@event.Id, [new RegistrantAttendanceRequest(registration.Id, [@event.Sessions.Single().Id])], Guid.NewGuid());
        await service.SubmitEvaluationAsync(member.UserId, registration.Id, rating: 5, comments: null);
        return (@event, registration, member);
    }

    /// <summary>Matches spec.md's "Certificate request before credit is earned is refused" - not
    /// yet EvaluationSubmitted.</summary>
    [Fact]
    public async Task GetCertificateDataAsync_BeforeEvaluationSubmitted_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        await service.UpdateAsync(@event.Id, ToUpdateRequest(@event) with { CpdUnitsOnsite = 8m });
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;

        var result = await service.GetCertificateDataAsync(member.UserId, registration.Id, isAdmin: false);

        Assert.False(result.Succeeded);
    }

    /// <summary>Matches spec.md's other "not yet available" case - unit value still null.</summary>
    [Fact]
    public async Task GetCertificateDataAsync_ApplicableUnitsStillNull_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var (@event, registration, member) = await SeedCreditedRegistrationAsync(db, service, onsiteUnits: 8m);
        // Correct the units back to null to simulate "never set for this modality".
        await service.UpdateAsync(@event.Id, ToUpdateRequest(@event) with { CpdUnitsOnsite = null });

        var result = await service.GetCertificateDataAsync(member.UserId, registration.Id, isAdmin: false);

        Assert.False(result.Succeeded);
    }

    /// <summary>Matches spec.md's "Certificate lists only attended sessions".</summary>
    [Fact]
    public async Task GetCertificateDataAsync_ListsOnlyAttendedSessions()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var sixSessions = Enumerable.Range(1, 6)
            .Select(i => new EventSessionRequest(i == 1 ? created.Sessions[0].Id : null, $"Lecture {i}", created.StartsAt.AddHours(i), created.StartsAt.AddHours(i + 1), i))
            .ToList();
        var @event = (await service.UpdateAsync(created.Id, ToUpdateRequest(created) with { Sessions = sixSessions, CpdUnitsOnsite = 8m })).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await MarkPaymentVerifiedAsync(db, registration.Id);
        var attendedIds = @event.Sessions.Take(3).Select(s => s.Id).ToList();
        await service.RecordAttendanceAsync(@event.Id, [new RegistrantAttendanceRequest(registration.Id, attendedIds)], Guid.NewGuid());
        await service.SubmitEvaluationAsync(member.UserId, registration.Id, rating: 5, comments: null);

        var result = await service.GetCertificateDataAsync(member.UserId, registration.Id, isAdmin: false);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Value!.AttendedSessionTitles.Count);
        Assert.Equal(4m, result.Value.CreditUnits); // 8 x 3/6
    }

    /// <summary>Matches spec.md's "Certificate reflects a corrected unit count" - computed at read
    /// time, so a later correction is visible on the very next call with no other action taken.</summary>
    [Fact]
    public async Task GetCertificateDataAsync_AfterUnitCorrection_ReflectsNewValue()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var (@event, registration, member) = await SeedCreditedRegistrationAsync(db, service, onsiteUnits: 8m);
        var before = await service.GetCertificateDataAsync(member.UserId, registration.Id, isAdmin: false);
        Assert.Equal(8m, before.Value!.CreditUnits);

        await service.UpdateAsync(@event.Id, ToUpdateRequest(@event) with { CpdUnitsOnsite = 6m });
        var after = await service.GetCertificateDataAsync(member.UserId, registration.Id, isAdmin: false);

        Assert.Equal(6m, after.Value!.CreditUnits);
    }

    [Fact]
    public async Task GetCertificateDataAsync_NotOwnerAndNotAdmin_Forbidden()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var (_, registration, _) = await SeedCreditedRegistrationAsync(db, service);

        var result = await service.GetCertificateDataAsync(Guid.NewGuid(), registration.Id, isAdmin: false);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultErrorType.Forbidden, result.ErrorType);
    }

    [Fact]
    public async Task GetCertificateDataAsync_Admin_BypassesOwnershipCheck()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var (_, registration, _) = await SeedCreditedRegistrationAsync(db, service);

        var result = await service.GetCertificateDataAsync(Guid.NewGuid(), registration.Id, isAdmin: true);

        Assert.True(result.Succeeded);
    }
```

- [ ] **Step 6: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter EventServiceTests`
Expected: FAIL to compile — `GetCertificateDataAsync` doesn't exist yet.

- [ ] **Step 7: Implement `GetCertificateDataAsync`**

```csharp
using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Events.Dtos;

namespace PSMPE.Portal.Application.Events;

public partial class EventService
{
    public async Task<Result<CertificateDataDto>> GetCertificateDataAsync(
        Guid userId, Guid registrationId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var registration = await db.EventRegistrations
            .Include(r => r.Member)
            .Include(r => r.Event).ThenInclude(e => e.Sessions)
            .FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken);
        if (registration is null)
        {
            return Result<CertificateDataDto>.NotFound($"Registration '{registrationId}' was not found.");
        }
        if (!isAdmin && registration.Member.UserId != userId)
        {
            return Result<CertificateDataDto>.Forbidden("This isn't your registration.");
        }

        var attendedSessionIds = await db.EventAttendances
            .Where(a => a.EventRegistrationId == registrationId)
            .Select(a => a.EventSessionId)
            .ToListAsync(cancellationToken);
        var totalSessions = registration.Event.Sessions.Count;
        var credit = CpdCredit.For(registration, registration.Event, attendedSessionIds.Count, totalSessions);
        if (credit is null)
        {
            return Result<CertificateDataDto>.Failure(
                "This registration hasn't earned CPD credit yet - the certificate isn't available.");
        }

        var attendedTitles = registration.Event.Sessions
            .Where(s => attendedSessionIds.Contains(s.Id))
            .OrderBy(s => s.Order)
            .Select(s => s.Title)
            .ToList();

        return Result<CertificateDataDto>.Success(new CertificateDataDto(
            $"{registration.Member.FirstName} {registration.Member.LastName}", registration.Event.Title,
            registration.Event.StartsAt, registration.Event.EndsAt, registration.Mode.ToString(),
            attendedTitles, credit.Value));
    }
}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter EventServiceTests`
Expected: PASS (all tests from Step 5 and prior tasks).

- [ ] **Step 9: Write the failing smoke test for `CertificatePdfGenerator`**

```csharp
using PSMPE.Portal.Application.Events;
using PSMPE.Portal.Application.Events.Dtos;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Events;

public class CertificatePdfGeneratorTests
{
    [Fact]
    public void Generate_ProducesNonEmptyPdfBytes()
    {
        var data = new CertificateDataDto(
            "Juan Dela Cruz", "Water Sanitation Workshop",
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow,
            "Onsite", ["Day 1: Opening", "Day 1: Cross-Connection Control"], 4m);

        var bytes = CertificatePdfGenerator.Generate(data);

        Assert.NotEmpty(bytes);
        // %PDF is the standard PDF magic number - a cheap sanity check that QuestPDF actually
        // produced a PDF and not, say, an exception swallowed somewhere.
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
```

- [ ] **Step 10: Run the test to verify it fails**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter CertificatePdfGeneratorTests`
Expected: FAIL to compile — `CertificatePdfGenerator` doesn't exist yet.

- [ ] **Step 11: Implement `CertificatePdfGenerator`**

```csharp
using PSMPE.Portal.Application.Events.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PSMPE.Portal.Application.Events;

/// <summary>
/// Renders a certificate on demand - never cached, never pre-generated (see
/// add-events-cpd-tracker/proposal.md). Called once per download request from
/// EventsController.GetCertificate, so a unit value corrected after the fact is reflected the very
/// next time someone downloads.
/// </summary>
public static class CertificatePdfGenerator
{
    public static byte[] Generate(CertificateDataDto data)
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
                    column.Item().AlignCenter().Text("Certificate of Completion").FontSize(28).Bold();
                    column.Item().PaddingTop(20).AlignCenter().Text($"This certifies that {data.MemberName}").FontSize(16);
                    column.Item().AlignCenter().Text($"attended {data.EventTitle}").FontSize(16);
                    column.Item().AlignCenter().Text(
                        $"{data.EventStartsAt:MMMM d, yyyy} - {data.EventEndsAt:MMMM d, yyyy} ({data.Mode})").FontSize(12);

                    column.Item().PaddingTop(20).Text("Sessions attended:").Bold();
                    foreach (var title in data.AttendedSessionTitles)
                    {
                        column.Item().Text($"- {title}");
                    }

                    column.Item().PaddingTop(20).AlignCenter().Text($"CPD Units Earned: {data.CreditUnits}").FontSize(16).Bold();
                });
            });
        });

        return document.GeneratePdf();
    }
}
```

- [ ] **Step 12: Run the test to verify it passes**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter CertificatePdfGeneratorTests`
Expected: PASS.

- [ ] **Step 13: Commit**

```bash
git add src/PSMPE.Portal.Application/PSMPE.Portal.Application.csproj src/PSMPE.Portal.WebAPI/Program.cs \
  src/PSMPE.Portal.Application/Events/ \
  tests/PSMPE.Portal.Application.UnitTests/Events/EventServiceTests.cs \
  tests/PSMPE.Portal.Application.UnitTests/Events/CertificatePdfGeneratorTests.cs
git commit -m "feat: generate CPD certificates on demand with QuestPDF"
```

---

## 12. `EventsController` and the `MembersController` CPD endpoint

**Files:**
- Modify: `src/PSMPE.Portal.Application/Payments/Dtos/PaymentDto.cs`
- Create: `src/PSMPE.Portal.WebAPI/Controllers/EventsController.cs`
- Modify: `src/PSMPE.Portal.WebAPI/Controllers/MembersController.cs`
- Test: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Events/EventsControllerTests.cs`

- [ ] **Step 1: Add `RecordCashPaymentRequest`**

In `src/PSMPE.Portal.Application/Payments/Dtos/PaymentDto.cs`, add:

```csharp
/// <summary>POST /api/events/registrations/{id}/payment/cash's request body - just the amount, no
/// proof file, no reference number. See PaymentService.RecordEventCashPaymentAsync.</summary>
public record RecordCashPaymentRequest(decimal Amount);
```

- [ ] **Step 2: Create `EventsController`**

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
/// Event Management and CPD Credit Tracking - see openspecs/events.md (Task 18) and
/// add-events-cpd-tracker/proposal.md for the full design. Payment verification/rejection for an
/// event registration's Payment happens through the existing PaymentsController endpoints
/// unchanged - only PaymentService's internals branch on Kind (see Task 9). This controller only
/// owns the two payment actions that are genuinely new: member proof submission and admin cash
/// recording, both scoped to a registration id rather than a bare payment id.
/// </summary>
[ApiController]
[Authorize]
[Route("api/events")]
public class EventsController(IEventService eventService, IPaymentService paymentService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<EventDto>>> GetAll(
        int page = 1, int pageSize = 20, string? search = null, string? chapter = null, bool upcomingOnly = false,
        CancellationToken cancellationToken = default) =>
        Ok(await eventService.GetAllAsync(page, pageSize, search, chapter, upcomingOnly, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EventDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var @event = await eventService.GetByIdAsync(id, cancellationToken);
        return @event is null ? NotFound() : Ok(@event);
    }

    [HttpPost]
    [RequirePermission(Permissions.Events.Manage)]
    public async Task<ActionResult<EventDto>> Create(CreateEventRequest request, CancellationToken cancellationToken)
    {
        var result = await eventService.CreateAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Events.Manage)]
    public async Task<ActionResult<EventDto>> Update(Guid id, UpdateEventRequest request, CancellationToken cancellationToken)
    {
        var result = await eventService.UpdateAsync(id, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToErrorActionResult(result);
    }

    [HttpPost("{id:guid}/register")]
    public async Task<ActionResult<EventRegistrationDto>> Register(Guid id, RegisterForEventRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await eventService.RegisterAsync(userId.Value, id, request.Mode, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToErrorActionResult(result);
    }

    [HttpPost("registrations/{id:guid}/cancel")]
    public async Task<IActionResult> CancelRegistration(Guid id, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        return ToActionResult(await eventService.CancelRegistrationAsync(userId.Value, id, cancellationToken));
    }

    [HttpPost("registrations/{id:guid}/payment")]
    public async Task<ActionResult<PaymentDto>> SubmitPayment(Guid id, SubmitPaymentRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await paymentService.SubmitForEventRegistrationAsync(userId.Value, id, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToErrorActionResult(result);
    }

    /// <summary>Admin-only. Reaches PaymentVerified in one call, with no proof file - see
    /// PaymentService.RecordEventCashPaymentAsync.</summary>
    [HttpPost("registrations/{id:guid}/payment/cash")]
    [RequirePermission(Permissions.Events.Manage)]
    public async Task<ActionResult<PaymentDto>> RecordCashPayment(Guid id, RecordCashPaymentRequest request, CancellationToken cancellationToken)
    {
        var decidedBy = CurrentUserId;
        if (decidedBy is null)
        {
            return Unauthorized();
        }

        var result = await paymentService.RecordEventCashPaymentAsync(id, request.Amount, decidedBy.Value, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToErrorActionResult(result);
    }

    /// <summary>Bulk roster reconciliation - one call covers every registrant an admin has worked
    /// through on the printed sign-in sheet, not just one. See EventService.RecordAttendanceAsync.</summary>
    [HttpPost("{id:guid}/roster/attendance")]
    [RequirePermission(Permissions.Events.Manage)]
    public async Task<IActionResult> RecordAttendance(Guid id, RecordAttendanceRequest request, CancellationToken cancellationToken)
    {
        var adminUserId = CurrentUserId;
        if (adminUserId is null)
        {
            return Unauthorized();
        }

        return ToActionResult(await eventService.RecordAttendanceAsync(id, request.Registrants, adminUserId.Value, cancellationToken));
    }

    [HttpPost("registrations/{id:guid}/evaluation")]
    public async Task<IActionResult> SubmitEvaluation(Guid id, SubmitEvaluationRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        return ToActionResult(await eventService.SubmitEvaluationAsync(userId.Value, id, request.Rating, request.Comments, cancellationToken));
    }

    [HttpGet("{id:guid}/roster")]
    [RequirePermission(Permissions.Events.View, Permissions.Events.Manage)]
    public async Task<ActionResult<EventRosterDto>> GetRoster(Guid id, CancellationToken cancellationToken)
    {
        var result = await eventService.GetRosterAsync(id, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToErrorActionResult(result);
    }

    /// <summary>Streams the PDF directly - never stored, generated fresh on every request (see
    /// CertificatePdfGenerator). Members may only fetch their own; staff need events:view or
    /// events:manage.</summary>
    [HttpGet("registrations/{id:guid}/certificate")]
    public async Task<IActionResult> GetCertificate(Guid id, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var isAdmin = User.HasClaim(Permissions.ClaimType, Permissions.Events.View) ||
                      User.HasClaim(Permissions.ClaimType, Permissions.Events.Manage);
        var result = await eventService.GetCertificateDataAsync(userId.Value, id, isAdmin, cancellationToken);
        if (!result.Succeeded)
        {
            return ToErrorActionResult(result);
        }

        var pdfBytes = CertificatePdfGenerator.Generate(result.Value!);
        return File(pdfBytes, "application/pdf", $"{result.Value!.EventTitle}-certificate.pdf");
    }

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    private IActionResult ToActionResult(Result result)
    {
        if (result.Succeeded)
        {
            return NoContent();
        }

        return ToErrorActionResult(result);
    }

    private IActionResult ToErrorActionResult(Result result) => result.ErrorType switch
    {
        ResultErrorType.NotFound => NotFound(new { message = result.Error }),
        ResultErrorType.Forbidden => Forbid(),
        ResultErrorType.Conflict => Conflict(new { message = result.Error }),
        _ => BadRequest(new { message = result.Error }),
    };
}
```

- [ ] **Step 3: Add `GET /api/members/me/cpd` to `MembersController`**

In `src/PSMPE.Portal.WebAPI/Controllers/MembersController.cs`, add `IEventService eventService` as a
new primary-constructor parameter:

```csharp
public class MembersController(
    IMemberService memberService, IMemberUploadService memberUploadService,
    IMemberCertificateService memberCertificateService, UserManager<ApplicationUser> userManager,
    IEmailSender emailSender, IPaymentService paymentService, IEventService eventService) : ControllerBase
```

Add `using PSMPE.Portal.Application.Events;` and `using PSMPE.Portal.Application.Events.Dtos;` to its
usings, and add the endpoint itself (e.g. near `GetMyProfile`):

```csharp
    /// <summary>Own registrations plus computed, prorated credit total - see
    /// EventService.GetMyCpdAsync. Reachable even while Expired, same as the other me/* reads.</summary>
    [HttpGet("me/cpd")]
    [AllowExpiredMember]
    public async Task<ActionResult<MyCpdSummaryDto>> GetMyCpd(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        return Ok(await eventService.GetMyCpdAsync(userId.Value, cancellationToken));
    }
```

- [ ] **Step 4: Build to confirm everything compiles**

Run: `dotnet build src/PSMPE.Portal.sln`
Expected: build succeeds (0 errors).

- [ ] **Step 5: Write the failing integration tests**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Events;

/// <summary>
/// Exercises the Event Management / CPD Tracker endpoints via real HTTP - authorization gating on
/// the admin-only actions, and one full member-side round trip (register, pay, get verified, get
/// attendance recorded, evaluate, read CPD, download a certificate) exercising the whole state
/// machine end to end. See add-events-cpd-tracker/specs/events/spec.md.
/// </summary>
public class EventsControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly HttpClient _client;

    public EventsControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _scope = factory.Services.CreateScope();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        _scope.Dispose();
        return Task.CompletedTask;
    }

    private Task<(Guid UserId, string Token)> CreateAdminAsync() =>
        _client.CreatePrivilegedUserAsync(_userManager, RoleNames.Admin);

    private HttpRequestMessage PostJson(string url, object body, string token) =>
        new(HttpMethod.Post, url) { Content = JsonContent.Create(body) }.WithBearer(token);

    private static object ValidEventPayload(string title = "Water Sanitation Workshop") => new
    {
        title,
        description = "Cross-connection control",
        chapter = Chapters.Ncr,
        venue = "PICC",
        startsAt = DateTimeOffset.UtcNow.AddDays(10),
        endsAt = DateTimeOffset.UtcNow.AddDays(10).AddHours(4),
        capacity = 100,
        fee = 500m,
    };

    [Fact]
    public async Task CreateEvent_WithoutEventsManage_ReturnsForbidden()
    {
        var memberToken = await _client.RegisterAndLoginAsync();

        var response = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), memberToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_AsAdmin_Succeeds()
    {
        var (_, adminToken) = await CreateAdminAsync();

        var response = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), adminToken));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("cpdUnitsOnsite").ValueKind == JsonValueKind.Null);
        Assert.Equal(1, body.GetProperty("sessions").GetArrayLength());
    }

    [Fact]
    public async Task RecordCashPayment_WithoutEventsManage_ReturnsForbidden()
    {
        var (_, adminToken) = await CreateAdminAsync();
        var createResponse = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), adminToken));
        var eventId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var memberToken = await _client.RegisterAndLoginAsync();
        var registerResponse = await _client.SendAsync(PostJson($"/api/events/{eventId}/register", new { mode = "Onsite" }, memberToken));
        var registrationId = (await registerResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await _client.SendAsync(
            PostJson($"/api/events/registrations/{registrationId}/payment/cash", new { amount = 500m }, memberToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FullRoundTrip_RegisterThroughCertificate_Succeeds()
    {
        var (adminUserId, adminToken) = await CreateAdminAsync();
        var createResponse = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), adminToken));
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var eventId = created.GetProperty("id").GetGuid();
        var sessionId = created.GetProperty("sessions")[0].GetProperty("id").GetGuid();

        var setUnits = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Put, $"/api/events/{eventId}")
        {
            Content = JsonContent.Create(new
            {
                title = created.GetProperty("title").GetString(),
                description = (string?)null,
                chapter = Chapters.Ncr,
                venue = "PICC",
                startsAt = created.GetProperty("startsAt").GetDateTimeOffset(),
                endsAt = created.GetProperty("endsAt").GetDateTimeOffset(),
                capacity = 100,
                fee = 500m,
                cpdUnitsOnsite = 8m,
                cpdUnitsOnline = (decimal?)null,
                sessions = new[] { new { id = sessionId, title = "Full Event", startsAt = created.GetProperty("startsAt").GetDateTimeOffset(), endsAt = created.GetProperty("endsAt").GetDateTimeOffset(), order = 1 } },
            }),
        }.WithBearer(adminToken));
        Assert.Equal(HttpStatusCode.OK, setUnits.StatusCode);

        var memberToken = await _client.RegisterAndLoginAsync();
        var registerResponse = await _client.SendAsync(PostJson($"/api/events/{eventId}/register", new { mode = "Onsite" }, memberToken));
        var registrationId = (await registerResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var cashResponse = await _client.SendAsync(
            PostJson($"/api/events/registrations/{registrationId}/payment/cash", new { amount = 500m }, adminToken));
        Assert.Equal(HttpStatusCode.OK, cashResponse.StatusCode);

        var attendanceResponse = await _client.SendAsync(PostJson(
            $"/api/events/{eventId}/roster/attendance",
            new { registrants = new[] { new { registrationId, sessionIds = new[] { sessionId } } } },
            adminToken));
        Assert.Equal(HttpStatusCode.NoContent, attendanceResponse.StatusCode);

        var evaluationResponse = await _client.SendAsync(PostJson(
            $"/api/events/registrations/{registrationId}/evaluation", new { rating = 5, comments = "Great" }, memberToken));
        Assert.Equal(HttpStatusCode.NoContent, evaluationResponse.StatusCode);

        var cpdResponse = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/members/me/cpd").WithBearer(memberToken));
        var cpdBody = await cpdResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(8m, cpdBody.GetProperty("totalCreditUnits").GetDecimal());

        var certificateResponse = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/events/registrations/{registrationId}/certificate").WithBearer(memberToken));
        Assert.Equal(HttpStatusCode.OK, certificateResponse.StatusCode);
        Assert.Equal("application/pdf", certificateResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetCertificate_BeforeEvaluationSubmitted_ReturnsBadRequest()
    {
        var (_, adminToken) = await CreateAdminAsync();
        var createResponse = await _client.SendAsync(PostJson("/api/events", ValidEventPayload(), adminToken));
        var eventId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var memberToken = await _client.RegisterAndLoginAsync();
        var registerResponse = await _client.SendAsync(PostJson($"/api/events/{eventId}/register", new { mode = "Onsite" }, memberToken));
        var registrationId = (await registerResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/events/registrations/{registrationId}/certificate").WithBearer(memberToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

- [ ] **Step 6: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter EventsControllerTests`
Expected: FAIL — either a compile error (before Steps 2–3 land) or 404s (`EventsController` route
not yet registered). Run this after Steps 2–4 above, once the project builds, to see genuine
assertion failures instead.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter EventsControllerTests`
Expected: PASS (6 tests).

- [ ] **Step 8: Run the full backend test suite**

Run: `dotnet test src/PSMPE.Portal.sln`
Expected: PASS — every prior Application unit test and every pre-existing integration test still
passes (in particular `PaymentServiceTests`'s `NewMembership`/`Renewal` tests, confirming Task 9's
`VerifyAsync`/`RejectAsync` changes didn't regress the membership-payment path).

- [ ] **Step 9: Commit**

```bash
git add src/PSMPE.Portal.Application/Payments/Dtos/PaymentDto.cs \
  src/PSMPE.Portal.WebAPI/Controllers/EventsController.cs src/PSMPE.Portal.WebAPI/Controllers/MembersController.cs \
  tests/PSMPE.Portal.WebAPI.IntegrationTests/Events/EventsControllerTests.cs
git commit -m "feat: add EventsController and the My CPD endpoint"
```

---

## 13. Frontend — `eventApi.ts`

**Files:**
- Create: `apps/web/src/core/api/endpoints/eventApi.ts`

No test runner for the frontend in this codebase — verification for every frontend task is
`tsc -b` / `eslint` plus a manual browser pass (see Task 18). This task is typed API-client plumbing
only, mirroring `paymentApi.ts`'s shape and conventions exactly (plain axios via the shared
`apiClient`, no react-query, `.then((res) => res.data)`).

- [ ] **Step 1: Create `eventApi.ts`**

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

export interface EventSession {
  id: string
  title: string
  startsAt: string
  endsAt: string
  order: number
}

export interface EventSessionInput {
  id: string | null
  title: string
  startsAt: string
  endsAt: string
  order: number
}

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
  /** Null means "TBD" - see Event.CpdUnitsOnsite's backend doc comment. */
  cpdUnitsOnsite: number | null
  cpdUnitsOnline: number | null
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
  fee: number
}

export interface UpdateEventRequest extends CreateEventRequest {
  cpdUnitsOnsite: number | null
  cpdUnitsOnline: number | null
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

- [ ] **Step 2: Type-check**

Run: `cd apps/web && npx tsc -b`
Expected: no new errors from `eventApi.ts`.

- [ ] **Step 3: Commit**

```bash
git add apps/web/src/core/api/endpoints/eventApi.ts
git commit -m "feat: add eventApi client module"
```

---

## 14. Frontend — Events list, detail, and registration

**Files:**
- Create: `apps/web/src/integrations/template/pages/EventsTable.tsx`
- Create: `apps/web/src/integrations/template/pages/EventFormModal.tsx`
- Create: `apps/web/src/integrations/template/pages/EventRegisterModal.tsx`
- Create: `apps/web/src/core/pages/EventsPage.tsx`
- Modify: `apps/web/src/integrations/template/index.ts`

Per this project's standing convention (every list/table needs search + filter, not just sorting),
`EventsTable` gets a title search box and a chapter filter dropdown, mirroring `MembersTable`'s
`searchInput`/`statusFilter` props exactly.

- [ ] **Step 1: Create `EventsTable`**

```typescript
import { Link } from 'react-router-dom'
import { LuCalendar, LuMapPin, LuPlus, LuSearch } from 'react-icons/lu'
import type { Event } from '../../../core/api/endpoints/eventApi'
import { Chapters, type ChapterValue } from '../../../core/types/member'
import { StandardButton } from '../components/shared/StandardButton'

interface EventsTableProps {
  events: Event[]
  canManageEvents: boolean
  searchInput: string
  onSearchInputChange: (value: string) => void
  chapterFilter: ChapterValue | null
  onChapterFilterChange: (chapter: ChapterValue | null) => void
  upcomingOnly: boolean
  onUpcomingOnlyChange: (value: boolean) => void
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
  onNewEvent: () => void
  onSelectEvent: (event: Event) => void
}

function formatCpdUnits(onsite: number | null, online: number | null) {
  if (onsite === null && online === null) return 'CPD units: TBD'
  return `CPD units: Onsite ${onsite ?? 'TBD'} / Online ${online ?? 'TBD'}`
}

export function EventsTable({
  events, canManageEvents, searchInput, onSearchInputChange, chapterFilter, onChapterFilterChange,
  upcomingOnly, onUpcomingOnlyChange, page, pageSize, totalCount, onPageChange, onNewEvent, onSelectEvent,
}: EventsTableProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))

  return (
    <div className="card">
      <div className="card-header flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <div className="relative">
            <LuSearch className="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-default-400" />
            <input
              type="text"
              className="input pl-9"
              placeholder="Search events..."
              value={searchInput}
              onChange={(e) => onSearchInputChange(e.target.value)}
            />
          </div>
          <select
            className="input"
            value={chapterFilter ?? ''}
            onChange={(e) => onChapterFilterChange((e.target.value || null) as ChapterValue | null)}
          >
            <option value="">All chapters</option>
            {Object.values(Chapters).map((c) => (
              <option key={c} value={c}>{c}</option>
            ))}
          </select>
          <label className="flex items-center gap-2 text-sm text-default-600">
            <input type="checkbox" checked={upcomingOnly} onChange={(e) => onUpcomingOnlyChange(e.target.checked)} />
            Upcoming only
          </label>
        </div>
        {canManageEvents && (
          <StandardButton onClick={onNewEvent} className="btn btn-primary btn-sm inline-flex items-center gap-1">
            <LuPlus className="size-4" /> New Event
          </StandardButton>
        )}
      </div>

      <div className="card-body p-0">
        {events.length === 0 ? (
          <p className="text-sm text-default-500 p-4">No events found.</p>
        ) : (
          <ul className="divide-y divide-default-200">
            {events.map((event) => (
              <li key={event.id} className="p-4 hover:bg-default-50 cursor-pointer" onClick={() => onSelectEvent(event)}>
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="font-medium text-default-800">{event.title}</p>
                    <p className="flex items-center gap-1 text-xs text-default-500 mt-1">
                      <LuCalendar className="size-3.5" />
                      {new Date(event.startsAt).toLocaleDateString()} - {new Date(event.endsAt).toLocaleDateString()}
                    </p>
                    {event.venue && (
                      <p className="flex items-center gap-1 text-xs text-default-500">
                        <LuMapPin className="size-3.5" /> {event.venue}
                      </p>
                    )}
                    <p className="text-xs text-default-500 mt-1">{formatCpdUnits(event.cpdUnitsOnsite, event.cpdUnitsOnline)}</p>
                  </div>
                  <div className="text-right shrink-0">
                    <p className="text-sm font-semibold">{event.fee > 0 ? `PHP ${event.fee.toFixed(2)}` : 'Free'}</p>
                    <p className="text-xs text-default-500">
                      {event.registeredCount}{event.capacity ? ` / ${event.capacity}` : ''} registered
                    </p>
                    {canManageEvents && (
                      <Link
                        to={`/events/${event.id}/roster`}
                        onClick={(e) => e.stopPropagation()}
                        className="text-xs text-primary hover:underline"
                      >
                        View roster
                      </Link>
                    )}
                  </div>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>

      <div className="card-footer flex items-center justify-between">
        <span className="text-xs text-default-500">Page {page} of {totalPages} ({totalCount} total)</span>
        <div className="flex gap-2">
          <button type="button" className="btn btn-sm" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>Previous</button>
          <button type="button" className="btn btn-sm" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}>Next</button>
        </div>
      </div>
    </div>
  )
}
```

- [ ] **Step 2: Create `EventFormModal`**

```typescript
import { useEffect, useState } from 'react'
import type { Event, EventSessionInput } from '../../../core/api/endpoints/eventApi'
import { eventApi } from '../../../core/api/endpoints/eventApi'
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
  return event?.sessions.map((s) => ({ id: s.id, title: s.title, startsAt: s.startsAt, endsAt: s.endsAt, order: s.order })) ?? []
}

/** Admin-only event create/edit, including session (lecture) management and setting each
 *  modality's CPD units - see EventService.UpdateAsync's session reconciliation on the backend. */
export function EventFormModal({ event, mode, onClose, onSaved }: EventFormModalProps) {
  const [title, setTitle] = useState(event?.title ?? '')
  const [description, setDescription] = useState(event?.description ?? '')
  const [chapter, setChapter] = useState(event?.chapter ?? '')
  const [venue, setVenue] = useState(event?.venue ?? '')
  const [startsAt, setStartsAt] = useState(event?.startsAt.slice(0, 16) ?? '')
  const [endsAt, setEndsAt] = useState(event?.endsAt.slice(0, 16) ?? '')
  const [capacity, setCapacity] = useState(event?.capacity?.toString() ?? '')
  const [fee, setFee] = useState(event?.fee.toString() ?? '0')
  const [cpdUnitsOnsite, setCpdUnitsOnsite] = useState(event?.cpdUnitsOnsite?.toString() ?? '')
  const [cpdUnitsOnline, setCpdUnitsOnline] = useState(event?.cpdUnitsOnline?.toString() ?? '')
  const [sessions, setSessions] = useState<EventSessionInput[]>(toSessionInputs(event))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setSessions(toSessionInputs(event))
  }, [event])

  const updateSession = (index: number, patch: Partial<EventSessionInput>) => {
    setSessions((prev) => prev.map((s, i) => (i === index ? { ...s, ...patch } : s)))
  }

  const addSession = () => {
    setSessions((prev) => [...prev, { id: null, title: '', startsAt, endsAt, order: prev.length + 1 }])
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
        fee: Number(fee),
      }

      if (mode === 'create') {
        await eventApi.createEvent(basePayload)
      } else if (event) {
        await eventApi.updateEvent(event.id, {
          ...basePayload,
          cpdUnitsOnsite: cpdUnitsOnsite ? Number(cpdUnitsOnsite) : null,
          cpdUnitsOnline: cpdUnitsOnline ? Number(cpdUnitsOnline) : null,
          sessions,
        })
      }
      onSaved()
    } catch (err) {
      setError(describeError(err, 'Could not save this event.'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
      <div className="card w-full max-w-2xl max-h-[90vh] overflow-y-auto">
        <div className="card-header">
          <h6 className="card-title">{mode === 'create' ? 'New Event' : 'Edit Event'}</h6>
        </div>
        <div className="card-body flex flex-col gap-3">
          {error && <p className="text-sm text-danger">{error}</p>}
          <input className="input" placeholder="Title" value={title} onChange={(e) => setTitle(e.target.value)} />
          <textarea className="input" placeholder="Description" value={description} onChange={(e) => setDescription(e.target.value)} />
          <div className="grid grid-cols-2 gap-3">
            <select className="input" value={chapter} onChange={(e) => setChapter(e.target.value)}>
              <option value="">National (all chapters)</option>
              {Object.values(Chapters).map((c) => <option key={c} value={c}>{c}</option>)}
            </select>
            <input className="input" placeholder="Venue" value={venue} onChange={(e) => setVenue(e.target.value)} />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <input type="datetime-local" className="input" value={startsAt} onChange={(e) => setStartsAt(e.target.value)} />
            <input type="datetime-local" className="input" value={endsAt} onChange={(e) => setEndsAt(e.target.value)} />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <input type="number" className="input" placeholder="Capacity" value={capacity} onChange={(e) => setCapacity(e.target.value)} />
            <input type="number" className="input" placeholder="Fee" value={fee} onChange={(e) => setFee(e.target.value)} />
          </div>

          {mode === 'edit' && (
            <>
              <div className="grid grid-cols-2 gap-3">
                <input type="number" step="0.01" className="input" placeholder="CPD Units (Onsite) - blank for TBD"
                  value={cpdUnitsOnsite} onChange={(e) => setCpdUnitsOnsite(e.target.value)} />
                <input type="number" step="0.01" className="input" placeholder="CPD Units (Online) - blank for TBD"
                  value={cpdUnitsOnline} onChange={(e) => setCpdUnitsOnline(e.target.value)} />
              </div>

              <div className="border-t border-default-200 pt-3">
                <div className="flex items-center justify-between mb-2">
                  <h6 className="text-sm font-semibold">Sessions / Lectures</h6>
                  <button type="button" className="btn btn-sm" onClick={addSession}>Add session</button>
                </div>
                {sessions.map((session, index) => (
                  <div key={session.id ?? `new-${index}`} className="grid grid-cols-[1fr_auto_auto_auto] gap-2 mb-2 items-center">
                    <input className="input" placeholder="Session title" value={session.title}
                      onChange={(e) => updateSession(index, { title: e.target.value })} />
                    <input type="datetime-local" className="input" value={session.startsAt.slice(0, 16)}
                      onChange={(e) => updateSession(index, { startsAt: new Date(e.target.value).toISOString() })} />
                    <input type="datetime-local" className="input" value={session.endsAt.slice(0, 16)}
                      onChange={(e) => updateSession(index, { endsAt: new Date(e.target.value).toISOString() })} />
                    <button type="button" className="btn btn-sm btn-danger" onClick={() => removeSession(index)}>Remove</button>
                  </div>
                ))}
              </div>
            </>
          )}
        </div>
        <div className="card-footer flex justify-end gap-2">
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
          <StandardButton onClick={handleSubmit} loading={saving} className="btn btn-primary">Save</StandardButton>
        </div>
      </div>
    </div>
  )
}
```

- [ ] **Step 3: Create `EventRegisterModal`**

```typescript
import { useState } from 'react'
import type { Event } from '../../../core/api/endpoints/eventApi'
import { EventMode, type EventModeValue, eventApi } from '../../../core/api/endpoints/eventApi'
import { describeError } from '../../../core/utils/apiError'
import { StandardButton } from '../components/shared/StandardButton'

interface EventRegisterModalProps {
  event: Event
  onClose: () => void
  onRegistered: () => void
}

/** Member-facing: pick a modality, register, then optionally submit payment proof right away
 *  (the member can also come back to it later from My CPD - registering alone is enough to hold
 *  the Registered row). */
export function EventRegisterModal({ event, onClose, onRegistered }: EventRegisterModalProps) {
  const [mode, setMode] = useState<EventModeValue>(EventMode.Onsite)
  const [amount, setAmount] = useState(event.fee.toString())
  const [referenceNo, setReferenceNo] = useState('')
  const [paidOn, setPaidOn] = useState(new Date().toISOString().slice(0, 10))
  const [proofFile, setProofFile] = useState<File | null>(null)
  const [registrationId, setRegistrationId] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleRegister = async () => {
    setSaving(true)
    setError(null)
    try {
      const registration = await eventApi.register(event.id, mode)
      setRegistrationId(registration.id)
      if (event.fee <= 0) {
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
        await eventApi.uploadPaymentProof((payment as { id: string }).id, proofFile)
      }
      onRegistered()
    } catch (err) {
      setError(describeError(err, 'Could not submit your payment.'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
      <div className="card w-full max-w-md">
        <div className="card-header">
          <h6 className="card-title">Register for {event.title}</h6>
        </div>
        <div className="card-body flex flex-col gap-3">
          {error && <p className="text-sm text-danger">{error}</p>}

          {!registrationId ? (
            <>
              <label className="flex items-center gap-2">
                <input type="radio" checked={mode === EventMode.Onsite} onChange={() => setMode(EventMode.Onsite)} />
                Onsite {event.cpdUnitsOnsite !== null ? `(${event.cpdUnitsOnsite} CPD units)` : '(CPD units: TBD)'}
              </label>
              <label className="flex items-center gap-2">
                <input type="radio" checked={mode === EventMode.Online} onChange={() => setMode(EventMode.Online)} />
                Online {event.cpdUnitsOnline !== null ? `(${event.cpdUnitsOnline} CPD units)` : '(CPD units: TBD)'}
              </label>
              <p className="text-sm text-default-600">Fee: {event.fee > 0 ? `PHP ${event.fee.toFixed(2)}` : 'Free'}</p>
            </>
          ) : (
            <>
              <p className="text-sm text-default-600">You're registered. Submit your payment proof to move to verification:</p>
              <input type="number" className="input" placeholder="Amount" value={amount} onChange={(e) => setAmount(e.target.value)} />
              <input className="input" placeholder="Reference No." value={referenceNo} onChange={(e) => setReferenceNo(e.target.value)} />
              <input type="date" className="input" value={paidOn} onChange={(e) => setPaidOn(e.target.value)} />
              <input type="file" accept="image/*,.pdf" onChange={(e) => setProofFile(e.target.files?.[0] ?? null)} />
            </>
          )}
        </div>
        <div className="card-footer flex justify-end gap-2">
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
          {!registrationId ? (
            <StandardButton onClick={handleRegister} loading={saving} className="btn btn-primary">Register</StandardButton>
          ) : (
            <StandardButton onClick={handleSubmitPayment} loading={saving} className="btn btn-primary">Submit Payment</StandardButton>
          )}
        </div>
      </div>
    </div>
  )
}
```

- [ ] **Step 4: Create `EventsPage`**

```typescript
import { useCallback, useEffect, useState } from 'react'
import type { Event } from '../api/endpoints/eventApi'
import { eventApi } from '../api/endpoints/eventApi'
import type { ChapterValue } from '../types/member'
import { describeError } from '../utils/apiError'
import { useAuth } from '../auth/useAuth'
import { Roles } from '../types/auth'
import { EventFormModal, EventRegisterModal, EventsTable, PageBreadcrumb, PageMeta } from '../../integrations/template'

const PAGE_SIZE = 20

export function EventsPage() {
  const { user } = useAuth()
  const canManageEvents = user?.roles.includes(Roles.Admin) || user?.roles.includes(Roles.SuperAdmin) || false
  const isMember = user?.roles.includes(Roles.Member) || false

  const [events, setEvents] = useState<Event[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [page, setPage] = useState(1)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [chapterFilter, setChapterFilter] = useState<ChapterValue | null>(null)
  const [upcomingOnly, setUpcomingOnly] = useState(true)

  const [formEvent, setFormEvent] = useState<{ event: Event | null; mode: 'create' | 'edit' } | null>(null)
  const [registeringEvent, setRegisteringEvent] = useState<Event | null>(null)

  useEffect(() => {
    const timer = setTimeout(() => {
      setSearch(searchInput)
      setPage(1)
    }, 350)
    return () => clearTimeout(timer)
  }, [searchInput])

  const fetchEvents = useCallback(
    async (isStale: () => boolean = () => false) => {
      const result = await eventApi.getEvents({ page, pageSize: PAGE_SIZE, search: search || undefined, chapter: chapterFilter ?? undefined, upcomingOnly })
      if (isStale()) return
      setEvents(result.items)
      setTotalCount(result.totalCount)
    },
    [page, search, chapterFilter, upcomingOnly],
  )

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)
    fetchEvents(() => cancelled)
      .catch((err) => {
        if (!cancelled) setError(describeError(err, 'Could not load events. Please try again.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [fetchEvents])

  const handleSelectEvent = (event: Event) => {
    if (canManageEvents) {
      setFormEvent({ event, mode: 'edit' })
    } else if (isMember) {
      setRegisteringEvent(event)
    }
  }

  return (
    <>
      <PageMeta title="Events" />
      <main>
        <PageBreadcrumb title="Events" />

        {error && <p className="text-sm text-danger mb-4">{error}</p>}

        {loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          <EventsTable
            events={events}
            canManageEvents={canManageEvents}
            searchInput={searchInput}
            onSearchInputChange={setSearchInput}
            chapterFilter={chapterFilter}
            onChapterFilterChange={(c) => { setChapterFilter(c); setPage(1) }}
            upcomingOnly={upcomingOnly}
            onUpcomingOnlyChange={(v) => { setUpcomingOnly(v); setPage(1) }}
            page={page}
            pageSize={PAGE_SIZE}
            totalCount={totalCount}
            onPageChange={setPage}
            onNewEvent={() => setFormEvent({ event: null, mode: 'create' })}
            onSelectEvent={handleSelectEvent}
          />
        )}

        {formEvent && (
          <EventFormModal
            event={formEvent.event}
            mode={formEvent.mode}
            onClose={() => setFormEvent(null)}
            onSaved={() => { setFormEvent(null); fetchEvents() }}
          />
        )}

        {registeringEvent && (
          <EventRegisterModal
            event={registeringEvent}
            onClose={() => setRegisteringEvent(null)}
            onRegistered={() => { setRegisteringEvent(null); fetchEvents() }}
          />
        )}
      </main>
    </>
  )
}
```

- [ ] **Step 5: Export the new template components**

In `apps/web/src/integrations/template/index.ts`, add after the `PaymentsQueueTable` export:

```typescript
export { EventsTable } from './pages/EventsTable'
export { EventFormModal } from './pages/EventFormModal'
export { EventRegisterModal } from './pages/EventRegisterModal'
```

- [ ] **Step 6: Type-check**

Run: `cd apps/web && npx tsc -b`
Expected: no new errors.

- [ ] **Step 7: Commit**

```bash
git add apps/web/src/integrations/template/pages/EventsTable.tsx apps/web/src/integrations/template/pages/EventFormModal.tsx \
  apps/web/src/integrations/template/pages/EventRegisterModal.tsx apps/web/src/core/pages/EventsPage.tsx \
  apps/web/src/integrations/template/index.ts
git commit -m "feat: add Events list, admin form, and member registration pages"
```

---

## 15. Frontend — admin event roster

**Files:**
- Create: `apps/web/src/integrations/template/pages/EventRosterTable.tsx`
- Create: `apps/web/src/core/pages/EventRosterPage.tsx`
- Modify: `apps/web/src/integrations/template/index.ts`

- [ ] **Step 1: Create `EventRosterTable`**

```typescript
import { useState } from 'react'
import type { EventRosterEntry, EventSession } from '../../../core/api/endpoints/eventApi'
import { StandardButton } from '../components/shared/StandardButton'

interface EventRosterTableProps {
  sessions: EventSession[]
  registrants: EventRosterEntry[]
  pendingAttendance: Record<string, Set<string>>
  onToggleSession: (registrationId: string, sessionId: string) => void
  onSaveAttendance: () => void
  savingAttendance: boolean
  onRecordCashPayment: (registrationId: string) => void
}

function paymentBadge(entry: EventRosterEntry) {
  if (!entry.paymentStatus) return <span className="text-xs text-default-400">No payment</span>
  const label = entry.paymentIsCash ? `${entry.paymentStatus} (cash)` : entry.paymentStatus
  const cls = entry.paymentStatus === 'Verified' ? 'bg-success/10 text-success'
    : entry.paymentStatus === 'Rejected' ? 'bg-danger/10 text-danger' : 'bg-warning/10 text-warning'
  return <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs ${cls}`}>{label}</span>
}

/** Per-session checkboxes reflect `pendingAttendance` (the in-progress edit), not
 *  `entry.attendedSessionIds` directly - EventRosterPage seeds pendingAttendance from the fetched
 *  roster and only writes it back to the server when the admin clicks Save, so partially-checked
 *  work isn't lost mid-reconciliation across a slow page. */
export function EventRosterTable({
  sessions, registrants, pendingAttendance, onToggleSession, onSaveAttendance, savingAttendance, onRecordCashPayment,
}: EventRosterTableProps) {
  const [cashAmount, setCashAmount] = useState<Record<string, string>>({})

  return (
    <div className="card">
      <div className="card-header flex items-center justify-between">
        <h6 className="card-title">Roster</h6>
        <StandardButton onClick={onSaveAttendance} loading={savingAttendance} className="btn btn-primary btn-sm">
          Save Attendance
        </StandardButton>
      </div>
      <div className="card-body overflow-x-auto">
        <table className="table">
          <thead>
            <tr>
              <th>Member</th>
              <th>Mode</th>
              <th>Payment</th>
              {sessions.map((s) => <th key={s.id} className="text-center whitespace-nowrap">{s.title}</th>)}
              <th>Status</th>
              <th>Evaluation</th>
              <th>Credit</th>
            </tr>
          </thead>
          <tbody>
            {registrants.map((entry) => (
              <tr key={entry.registrationId}>
                <td>
                  <div>{entry.memberName}</div>
                  <div className="text-xs text-default-400">{entry.membershipNo ?? '-'}</div>
                </td>
                <td>{entry.mode}</td>
                <td>
                  <div className="flex flex-col gap-1">
                    {paymentBadge(entry)}
                    {!entry.paymentId || entry.paymentStatus === 'Rejected' ? (
                      <div className="flex gap-1">
                        <input
                          type="number"
                          className="input input-sm w-20"
                          placeholder="Amount"
                          value={cashAmount[entry.registrationId] ?? ''}
                          onChange={(e) => setCashAmount((prev) => ({ ...prev, [entry.registrationId]: e.target.value }))}
                        />
                        <button type="button" className="btn btn-sm" onClick={() => onRecordCashPayment(entry.registrationId)}>
                          Record Cash
                        </button>
                      </div>
                    ) : null}
                  </div>
                </td>
                {sessions.map((s) => (
                  <td key={s.id} className="text-center">
                    <input
                      type="checkbox"
                      disabled={entry.paymentStatus !== 'Verified'}
                      checked={pendingAttendance[entry.registrationId]?.has(s.id) ?? false}
                      onChange={() => onToggleSession(entry.registrationId, s.id)}
                    />
                  </td>
                ))}
                <td>{entry.status}</td>
                <td>{entry.evaluationRating ?? '-'}</td>
                <td>{entry.creditUnits ?? '-'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
```

- [ ] **Step 2: Create `EventRosterPage`**

```typescript
import { useCallback, useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import type { EventRoster } from '../api/endpoints/eventApi'
import { eventApi } from '../api/endpoints/eventApi'
import { describeError } from '../utils/apiError'
import { EventRosterTable, PageBreadcrumb, PageMeta } from '../../integrations/template'

export function EventRosterPage() {
  const { id } = useParams<{ id: string }>()
  const [roster, setRoster] = useState<EventRoster | null>(null)
  const [pendingAttendance, setPendingAttendance] = useState<Record<string, Set<string>>>({})
  const [loading, setLoading] = useState(true)
  const [savingAttendance, setSavingAttendance] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchRoster = useCallback(async () => {
    if (!id) return
    const result = await eventApi.getRoster(id)
    setRoster(result)
    setPendingAttendance(
      Object.fromEntries(result.registrants.map((r) => [r.registrationId, new Set(r.attendedSessionIds)])),
    )
  }, [id])

  useEffect(() => {
    setLoading(true)
    setError(null)
    fetchRoster()
      .catch((err) => setError(describeError(err, 'Could not load the roster.')))
      .finally(() => setLoading(false))
  }, [fetchRoster])

  const handleToggleSession = (registrationId: string, sessionId: string) => {
    setPendingAttendance((prev) => {
      const next = new Set(prev[registrationId] ?? [])
      if (next.has(sessionId)) next.delete(sessionId)
      else next.add(sessionId)
      return { ...prev, [registrationId]: next }
    })
  }

  const handleSaveAttendance = async () => {
    if (!id) return
    setSavingAttendance(true)
    setError(null)
    try {
      const registrants = Object.entries(pendingAttendance).map(([registrationId, sessionIds]) => ({
        registrationId,
        sessionIds: [...sessionIds],
      }))
      await eventApi.recordAttendance(id, registrants)
      await fetchRoster()
    } catch (err) {
      setError(describeError(err, 'Could not save attendance.'))
    } finally {
      setSavingAttendance(false)
    }
  }

  const handleRecordCashPayment = async (registrationId: string, amount: number) => {
    setError(null)
    try {
      await eventApi.recordCashPayment(registrationId, amount)
      await fetchRoster()
    } catch (err) {
      setError(describeError(err, 'Could not record this cash payment.'))
    }
  }

  return (
    <>
      <PageMeta title="Event Roster" />
      <main>
        <PageBreadcrumb title={roster ? `Roster: ${roster.eventTitle}` : 'Roster'} />

        {error && <p className="text-sm text-danger mb-4">{error}</p>}

        {loading || !roster ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          <EventRosterTable
            sessions={roster.sessions}
            registrants={roster.registrants}
            pendingAttendance={pendingAttendance}
            onToggleSession={handleToggleSession}
            onSaveAttendance={handleSaveAttendance}
            savingAttendance={savingAttendance}
            onRecordCashPayment={(registrationId) => handleRecordCashPayment(registrationId, 0)}
          />
        )}
      </main>
    </>
  )
}
```

Note: `onRecordCashPayment` in `EventRosterTable` reads its amount from the row's own local input
state, so `EventRosterPage` doesn't need to know the amount itself — Step 3 below wires that
through properly rather than the placeholder `0` shown here.

- [ ] **Step 3: Thread the cash amount from the table's local input up to the page**

Change `EventRosterTable`'s `onRecordCashPayment` prop type to
`(registrationId: string, amount: number) => void`, and in its "Record Cash" button's `onClick`,
change it to:

```typescript
                        <button
                          type="button"
                          className="btn btn-sm"
                          onClick={() => onRecordCashPayment(entry.registrationId, Number(cashAmount[entry.registrationId] ?? '0'))}
                        >
                          Record Cash
                        </button>
```

And in `EventRosterPage`, change the prop wiring to pass the handler through directly:

```typescript
            onRecordCashPayment={handleRecordCashPayment}
```

- [ ] **Step 4: Export `EventRosterTable` and add the route**

In `apps/web/src/integrations/template/index.ts`, add:

```typescript
export { EventRosterTable } from './pages/EventRosterTable'
```

(The route itself, `/events/:id/roster`, is added in Task 17 alongside the rest of this feature's
routing.)

- [ ] **Step 5: Type-check**

Run: `cd apps/web && npx tsc -b`
Expected: no new errors.

- [ ] **Step 6: Commit**

```bash
git add apps/web/src/integrations/template/pages/EventRosterTable.tsx apps/web/src/core/pages/EventRosterPage.tsx \
  apps/web/src/integrations/template/index.ts
git commit -m "feat: add admin event roster page"
```

---

## 16. Frontend — "My CPD" page

**Files:**
- Create: `apps/web/src/integrations/template/pages/MyCpdTable.tsx`
- Create: `apps/web/src/core/pages/MyCpdPage.tsx`
- Modify: `apps/web/src/integrations/template/index.ts`

- [ ] **Step 1: Create `MyCpdTable`**

```typescript
import { LuDownload } from 'react-icons/lu'
import type { MyCpdRegistration } from '../../../core/api/endpoints/eventApi'
import { eventApi } from '../../../core/api/endpoints/eventApi'

interface MyCpdTableProps {
  registrations: MyCpdRegistration[]
}

async function handleDownload(registrationId: string) {
  const result = await eventApi.downloadCertificate(registrationId)
  if (!result) return
  window.open(result.url, '_blank')
}

export function MyCpdTable({ registrations }: MyCpdTableProps) {
  if (registrations.length === 0) {
    return <p className="text-sm text-default-500">You haven't registered for any events yet.</p>
  }

  return (
    <div className="card">
      <div className="card-body overflow-x-auto">
        <table className="table">
          <thead>
            <tr>
              <th>Event</th>
              <th>Mode</th>
              <th>Status</th>
              <th>Sessions Attended</th>
              <th>Credit Earned</th>
              <th>Certificate</th>
            </tr>
          </thead>
          <tbody>
            {registrations.map((r) => (
              <tr key={r.registrationId}>
                <td>
                  <div>{r.eventTitle}</div>
                  <div className="text-xs text-default-400">{new Date(r.eventStartsAt).toLocaleDateString()}</div>
                </td>
                <td>{r.mode}</td>
                <td>{r.status}</td>
                <td>{r.sessionsAttended} / {r.totalSessions}</td>
                <td>{r.creditUnits ?? '-'}</td>
                <td>
                  {r.creditUnits !== null ? (
                    <button type="button" className="btn btn-sm inline-flex items-center gap-1" onClick={() => handleDownload(r.registrationId)}>
                      <LuDownload className="size-3.5" /> Download
                    </button>
                  ) : (
                    <span className="text-xs text-default-400">Not yet available</span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
```

- [ ] **Step 2: Create `MyCpdPage`**

```typescript
import { useEffect, useState } from 'react'
import type { MyCpdSummary } from '../api/endpoints/eventApi'
import { eventApi } from '../api/endpoints/eventApi'
import { describeError } from '../utils/apiError'
import { StatTile } from '../../integrations/template/components/shared/StatTile'
import { MyCpdTable, PageBreadcrumb, PageMeta } from '../../integrations/template'
import { LuAward } from 'react-icons/lu'

export function MyCpdPage() {
  const [summary, setSummary] = useState<MyCpdSummary | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    eventApi.getMyCpd()
      .then((result) => {
        if (!cancelled) setSummary(result)
      })
      .catch((err) => {
        if (!cancelled) setError(describeError(err, 'Could not load your CPD history.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  return (
    <>
      <PageMeta title="My CPD" />
      <main>
        <PageBreadcrumb title="My CPD" />

        {error && <p className="text-sm text-danger mb-4">{error}</p>}

        {loading || !summary ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          <div className="flex flex-col gap-4">
            <StatTile icon={LuAward} label="Total CPD units earned" value={summary.totalCreditUnits} accent="bg-primary/15 text-primary" />
            <MyCpdTable registrations={summary.registrations} />
          </div>
        )}
      </main>
    </>
  )
}
```

- [ ] **Step 3: Export `MyCpdTable`**

In `apps/web/src/integrations/template/index.ts`, add:

```typescript
export { MyCpdTable } from './pages/MyCpdTable'
```

- [ ] **Step 4: Type-check**

Run: `cd apps/web && npx tsc -b`
Expected: no new errors.

- [ ] **Step 5: Commit**

```bash
git add apps/web/src/integrations/template/pages/MyCpdTable.tsx apps/web/src/core/pages/MyCpdPage.tsx \
  apps/web/src/integrations/template/index.ts
git commit -m "feat: add My CPD page"
```

---

## 17. Routing, nav, and removing the mock widget

**Files:**
- Modify: `apps/web/src/core/routes/router.tsx`
- Modify: `apps/web/src/integrations/template/components/layout/SideNav/menu.ts`
- Modify: `apps/web/src/integrations/template/pages/DashboardPage.tsx`
- Delete: `apps/web/src/integrations/template/components/dashboard-previews/EventsPreviewWidget.tsx`

- [ ] **Step 1: Add the new routes**

In `apps/web/src/core/routes/router.tsx`, add imports:

```typescript
import { EventsPage } from '../pages/EventsPage'
import { EventRosterPage } from '../pages/EventRosterPage'
import { MyCpdPage } from '../pages/MyCpdPage'
```

Add `{ path: '/events', element: <EventsPage /> }` and `{ path: '/my-cpd', element: <MyCpdPage /> }`
inside the main `<AppShell />` children array (next to `{ path: '/content', element: <ContentListPage /> }`),
reachable by any authenticated user - the events list itself has no admin-only gate (Admins manage
inline via the same page; Members register from it), and My CPD is entirely self-service.

Add `{ path: '/events/:id/roster', element: <EventRosterPage /> }` inside the existing
`Roles.Admin, Roles.SuperAdmin, Roles.Approval`-gated `<ProtectedRoute>` block (the same one wrapping
`/members`, `/admin/users`, etc.) — the roster reveals payment and personal attendance data, so it
needs the same admin-tier gate as the rest of that block, even though the finer-grained
`events:manage`/`events:view` split (Task 3) is what the API itself actually enforces.

- [ ] **Step 2: Add the nav entries**

In `apps/web/src/integrations/template/components/layout/SideNav/menu.ts`, add a new `LuCalendarClock`
icon import, and two entries — one in the "Membership" section for the member-facing "My CPD" link,
one for the "Events" admin link:

```typescript
import {
  LuBanknote,
  LuBellRing,
  LuCalendarClock,
  LuFileClock,
  LuFileText,
  LuMonitorDot,
  LuShieldCheck,
  LuSquareUserRound,
  LuUserRound,
  LuUsers,
} from 'react-icons/lu'
```

Add after the `MyProfile` entry:

```typescript
  {
    key: 'MyCpd',
    label: 'My CPD',
    icon: LuCalendarClock,
    href: '/my-cpd',
    requiredRoles: ['Member'],
  },
```

Add after the `Members` entry:

```typescript
  {
    key: 'Events',
    label: 'Events',
    icon: LuCalendarClock,
    href: '/events',
  },
```

(No `requiredRoles` on `Events` — every authenticated user, staff or member, lands on the same list;
the page itself decides whether to show admin actions via `canManageEvents`, same pattern as
`Content`.)

- [ ] **Step 3: Remove the mock widget from the Dashboard**

In `apps/web/src/integrations/template/pages/DashboardPage.tsx`, remove the import
`import { EventsPreviewWidget } from '../components/dashboard-previews/EventsPreviewWidget'` and
the `<EventsPreviewWidget />` usage (the "Replace/delete this whole component once the real module
ships" comment on the widget itself is exactly this step). If the surrounding layout used
`EventsPreviewWidget` as one half of a two-column row alongside `NewsPreviewWidget` (see the recent
"rearrange Dashboard into a 2-column layout" commit), replace it with a small real widget instead of
leaving an empty column — a compact "Upcoming Events" card reusing `eventApi.getEvents` with
`upcomingOnly: true, pageSize: 4` is enough; do not leave the column blank or reintroduce mock data.

- [ ] **Step 4: Delete the mock widget file**

```bash
git rm apps/web/src/integrations/template/components/dashboard-previews/EventsPreviewWidget.tsx
```

- [ ] **Step 5: Type-check and lint**

Run: `cd apps/web && npx tsc -b && npx eslint .`
Expected: no errors (in particular, no lingering import of the deleted `EventsPreviewWidget`).

- [ ] **Step 6: Manual browser pass**

Run the app locally (`docker compose up` or the project's usual dev script) and, logged in as the
seeded Admin account, confirm: `/events` lists events and lets you create one; editing an event lets
you set CPD units and add/remove sessions; `/events/:id/roster` shows registrants once someone has
registered. Logged in as the seeded Member account, confirm: `/events` lets you register and submit
payment proof; `/my-cpd` shows your registrations and a certificate download once credit is earned;
the Dashboard no longer shows the "Preview · Coming Soon" Events card.

- [ ] **Step 7: Commit**

```bash
git add apps/web/src/core/routes/router.tsx apps/web/src/integrations/template/components/layout/SideNav/menu.ts \
  apps/web/src/integrations/template/pages/DashboardPage.tsx
git commit -m "feat: wire up Events routes/nav and remove the dashboard mock widget"
```

---

## 18. Final verification and docs

**Files:**
- Create: `openspecs/events.md`
- Modify: `openspecs/payments.md`

Per this project's own standing convention (new/changed endpoints must update the matching
`openspecs/<feature>.md`, not just code), this feature needs a new `openspecs/events.md` and an
update to `openspecs/payments.md` for the `Kind` extension — read `openspecs/README.md` first for
the expected shape of a living doc in this repo, and `openspecs/members.md`/`openspecs/payments.md`
for tone/structure to match (Purpose, Endpoints, then prose sections on the non-obvious decisions).

- [ ] **Step 1: Run the full backend test suite**

Run: `dotnet test src/PSMPE.Portal.sln`
Expected: PASS — every Application unit test (`EventServiceTests`, `CpdCreditTests`,
`CertificatePdfGeneratorTests`, `PaymentServiceTests`) and every WebAPI integration test
(`EventsControllerTests` plus every pre-existing suite) passes.

- [ ] **Step 2: Run the full frontend build**

Run: `cd apps/web && npx tsc -b && npx eslint . && npm run build`
Expected: no type errors, no lint errors, build succeeds.

- [ ] **Step 3: Re-read `specs/events/spec.md` scenario by scenario and confirm each is covered**

Go through every `#### Scenario:` heading in
`openspec/changes/add-events-cpd-tracker/specs/events/spec.md` and confirm a test from Tasks 4–12
exercises it. As of this plan, the mapping is:

| Scenario | Test |
|---|---|
| A member registers for an event with units not yet set | `CreateAsync_ValidRequest_StartsWithBothCpdUnitsNull` (Task 4) |
| One modality's units are set while the other remains TBD | `UpdateAsync_SetsOneModalitysUnitsWhileTheOtherStaysTbd` (Task 4) |
| CPD units are set after the event has already happened | `UpdateAsync_EventAlreadyEnded_CanStillSetCpdUnits` (Task 4) |
| A member cannot register twice for the same event | `RegisterAsync_Twice_SecondCallFailsEvenUnderADifferentMode` (Task 5) |
| Attendance cannot be recorded before payment is verified | `RecordAttendanceAsync_BeforePaymentVerified_Fails` (Task 6) |
| Verifying an event payment advances the registration | `VerifyAsync_EventRegistrationPayment_MovesRegistrationToPaymentVerified` (Task 9) |
| A rejected event payment can be resubmitted | `RejectAsync_EventRegistrationPayment_SetsRegistrationRejectedAndAllowsResubmission` (Task 9) |
| An admin records a cash payment | `RecordEventCashPaymentAsync_Valid_CreatesVerifiedPaymentAndMovesRegistration` (Task 9) |
| A cash payment cannot be recorded over an existing payment | `RecordEventCashPaymentAsync_RegistrationAlreadyHasSubmittedPayment_Fails` (Task 9) |
| An admin reconciles roster attendance after the event | `RecordAttendanceAsync_RecordsSessions_MovesRegistrationToAttended` (Task 6) |
| A member attends only part of a multi-session event | `RecordAttendanceAsync_PartialAttendance_RecordsExactlyThatManySessions` (Task 6) |
| Attendance cannot be recorded against a session from a different event | `RecordAttendanceAsync_SessionFromDifferentEvent_Fails` (Task 6) |
| Evaluation is blocked before attendance | `SubmitEvaluationAsync_BeforeAttended_Fails` (Task 7) |
| A member completes an event | `SubmitEvaluationAsync_AfterAttended_MovesToEvaluationSubmitted` (Task 7) |
| A member's CPD total reflects only completed, credited registrations | `GetMyCpdAsync_SumsOnlyEvaluationSubmittedRegistrationsWithNonNullUnits` (Task 8) |
| Partial attendance earns prorated credit | `For_PartialAttendance_ReturnsProratedValue` (Task 8) |
| Onsite and Online registrations on the same event earn different credit | `For_FullAttendance_UsesUnitsForTheRegistrationsOwnMode` (Task 8) |
| Certificate request before credit is earned is refused | `GetCertificateDataAsync_BeforeEvaluationSubmitted_Fails` / `GetCertificateDataAsync_ApplicableUnitsStillNull_Fails` (Task 11) |
| Certificate reflects a corrected unit count | `GetCertificateDataAsync_AfterUnitCorrection_ReflectsNewValue` (Task 11) |
| Certificate lists only attended sessions | `GetCertificateDataAsync_ListsOnlyAttendedSessions` (Task 11) |

If any row's test is missing or was skipped during implementation, add it now before continuing —
do not close this task with a gap in that table.

- [ ] **Step 4: Write `openspecs/events.md`**

Create `openspecs/events.md` following `openspecs/payments.md`'s structure (Purpose, then an
Endpoints table, then prose sections on the decisions that aren't obvious from the code alone).
Cover, at minimum:
- Purpose: event management + CPD credit tracking, and that credit is computed, never stored.
- The full endpoint table from `proposal.md`'s "API endpoints" section, with role/permission per row.
- The `Event` → `EventSession` → `EventAttendance` shape and why attendance is per-session, not
  per-event (prorating).
- Why attendance is admin roster reconciliation, not member self-check-in.
- The `Mode` (Onsite/Online) split and how it selects which of `CpdUnitsOnsite`/`CpdUnitsOnline`
  applies.
- The CPD credit formula, computed at read time, with a worked example (matching the 8-unit,
  3-of-6-sessions example already used throughout `spec.md` and this plan).
- The two payment paths (member proof upload vs. admin cash) and that a registration has exactly
  one active `Payment` regardless of which path was used.
- A cross-reference to `openspecs/payments.md` for the shared `Payment`/`PaymentService` mechanics,
  and to `openspecs/members.md` for how `Member` relates to `EventRegistration`.
- A "Not Built" section mirroring `proposal.md`'s, so a future reader doesn't wonder whether CPD
  target tracking, event cancellation, or CPDAS integration were simply forgotten.

- [ ] **Step 5: Update `openspecs/payments.md`**

Add a short section (or extend the existing endpoint table) noting:
- `Payment.Kind` gained a third case, `EventRegistration`, with a nullable `EventRegistrationId` FK.
- `POST /{id}/verify` and `POST /{id}/reject` now branch on `Kind`: for an `EventRegistration`
  payment, verifying/rejecting drives the linked `EventRegistration.Status` instead of
  `MembershipStatus`/`RenewalDueDate` — see `EventPaymentVerification.Apply` vs.
  `PaymentVerification.Apply`.
- Two new endpoints exist under `/api/events/...` (not `/api/payments/...`) for the two genuinely
  new payment actions specific to events — member proof submission and admin cash recording — with
  a pointer to `openspecs/events.md` for their details, so this doesn't turn into a second full copy
  of that documentation.

- [ ] **Step 6: Final commit**

```bash
git add openspecs/events.md openspecs/payments.md
git commit -m "docs: add openspecs/events.md and document the Payment.Kind extension"
```

- [ ] **Step 7: Confirm the branch is ready for review**

Run: `git log --oneline main..HEAD` (or the equivalent against this branch's actual base) and
confirm every commit from Tasks 1–18 is present, then hand off per this repo's normal review/PR
process. This plan does not include a push or PR step — follow whatever the user's standing
instructions for this repo say about that (this project pushes straight to `develop`, per prior
project context; confirm with the user before pushing if that's ever in doubt).

---
