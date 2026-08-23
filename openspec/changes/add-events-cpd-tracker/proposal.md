# Change: Event Management & CPD Credit Tracker

## Status

**Brainstormed, not yet approved for implementation.** Designed 2026-08-23 through collaborative
brainstorming with the user, working through scope one question at a time. Several answers are the
user's own best judgment standing in for the actual PSMPE client — each is called out explicitly
under "Open Questions For The Client" below and should be confirmed before implementation starts.
No code exists for any of this yet.

## Why

PSMPE holds events and workshops (national conventions, chapter seminars, technical workshops) that
carry PRC CPD (Continuing Professional Development) credit. A member who attends and completes one
earns credit toward the unit requirement for their next PRC license renewal. `Member` already
carries `PrcLicenseNo`, `PrcRegistrationDate`, and `PrcValidUntilDate` — the renewal-cycle data this
feature ultimately feeds into — but there is no `Event` entity, no registration flow, and no credit
tracking anywhere in the codebase.

The only trace of this feature today is `EventsPreviewWidget.tsx` on the Dashboard: a static,
hardcoded mock card with fictional sample events, explicitly commented "Replace/delete this whole
component once the real module ships." The prior `add-membership-dashboard` proposal deferred this
exact scope: *"CPD Tracker — deferred, domain-coupled to Event Management."* This proposal is that
deferred work.

## Decisions

Each resolved by the user during brainstorming:

- **One combined proposal**, not split into separate Event-Management and CPD-Tracker changes —
  the two are tightly coupled (credit only exists because an event was attended and completed), and
  splitting them would mean building a CPD tracker with nothing yet to track.
- **Data model: one `EventRegistration` row per member per event**, with a `Status` enum walking
  forward (`Registered → PaymentSubmitted → PaymentVerified → Attended → EvaluationSubmitted`, plus
  `Rejected`/`Cancelled` off-ramps), rather than separate Registration/Attendance/Evaluation tables.
  Mirrors the existing `Payment` entity's single-row-with-status-enum pattern. Rejected the
  split-table alternative — nothing in this flow needs attendance or evaluation to exist
  independently of a registration; they're 1:1 by construction.
- **Completion = attendance + a member-submitted post-event evaluation form** (not a quiz with a
  passing score, not a bare one-click self-certify). The evaluation is a fixed set of fields
  (rating, comments), not admin-configurable per event, to keep this pass scoped.
- **Attendance = member self check-in on the day, with admin override/correction** for no-shows,
  walk-ins, or check-in problems — not purely staff-driven, not purely self-service.
- **CPD units are not fixed when the event is created.** `Event.CpdUnits` is nullable and shown as
  "TBD" until an admin sets it — which can happen before *or* after the event, since the actual
  accredited unit count is often only confirmed close to (or following) the session. Registration,
  payment, attendance, and evaluation all proceed normally regardless of whether units are set yet.
- **CPD credit is computed at read time, never stored.** A registration's earned credit is
  `Event.CpdUnits` once it's set, counted only if that registration reached `EvaluationSubmitted`.
  This mirrors `Payment.IsExpired` (a computed property, not a background job — there is no
  scheduler/`IHostedService` anywhere in this codebase apart from log retention). It also means a
  `CpdUnits` value set or corrected after the fact is instantly correct for every attendee, with no
  backfill step.
- **No CPD target/renewal-cycle progress tracking in this pass.** Members and admins see a running
  total of units earned, not a "9 / 15 required this cycle" comparison. Deferred per the user
  ("can be determined later") — the required-units-per-cycle number and how it maps to
  `PrcValidUntilDate` needs the client's input before it can be built correctly.
- **Event creation and management is Admin/staff-only.** No chapter-officer-level permission scope
  in this pass, even though `Member.ChapterPosition` exists and events are chapter-scoped by
  location — kept out to limit the size of this change.
- **Registration is paid, reusing the existing `Payment` entity and flow.** `Payment.Kind` gains an
  `EventRegistration` case and `Payment` gains a nullable `EventRegistrationId` FK; submission and
  admin verification work exactly as they do for membership dues today. See the flagged assumption
  below — this was the user's specific choice over a "mixed free/paid" option.
- **Certificate: a downloadable PDF, generated on demand**, only once the credit condition above is
  true (`EvaluationSubmitted` + `CpdUnits` set) — not pre-generated or stored ahead of time, so a
  corrected `CpdUnits` never leaves a stale certificate in circulation.

## What Changes

### 1. New `Event` entity (Domain)

`Title`, `Description`, `Chapter`, `Venue`, `StartsAt`, `EndsAt`, `Capacity` (int), `Fee` (decimal,
settable to 0 for a free event), `CpdUnits` (nullable int — null means "TBD").

### 2. New `EventRegistration` entity (Domain)

FKs to `Member` and `Event`. `Status` enum: `Registered`, `PaymentSubmitted`, `PaymentVerified`,
`Attended`, `EvaluationSubmitted`, `Rejected`, `Cancelled`. Evaluation fields captured directly on
the row: `EvaluationRating`, `EvaluationComments`, `EvaluationSubmittedAt`. `AttendedAt`,
`AttendedBy` (nullable — null when self check-in, set to the admin's user id on override) for an
audit trail of who marked attendance.

### 3. Payment integration

`Payment.Kind` gains an `EventRegistration` case; `Payment` gains a nullable
`EventRegistrationId` FK (mirrors how `Payment.MemberId` already works). The existing
`POST /api/payments/{id}/verify` endpoint is extended: when `Kind == EventRegistration`, verifying
advances the linked `EventRegistration.Status` from `PaymentSubmitted` to `PaymentVerified`, the
same way verifying a membership payment today flips `Member.Status` to `Active`.

### 4. API endpoints

| Endpoint | Role | Purpose |
|---|---|---|
| `GET /api/events` | Any authenticated | List events (upcoming/past) |
| `POST /api/events` | Admin | Create event (`CpdUnits` starts null) |
| `PUT /api/events/{id}` | Admin | Edit event details, including setting/correcting `CpdUnits` |
| `POST /api/events/{id}/register` | Member | Create `EventRegistration` (→ `Registered`) |
| `POST /api/events/registrations/{id}/payment` | Member | Submit proof (reuses payment-submit pattern) |
| `POST /api/events/registrations/{id}/check-in` | Member (self) or Admin | → `Attended` |
| `POST /api/events/registrations/{id}/evaluation` | Member | → `EvaluationSubmitted` |
| `GET /api/events/{id}/roster` | Admin | Full attendee list with per-stage status, for event-day/attendance work |
| `GET /api/members/me/cpd` | Member | Own registrations plus computed credit total |
| `GET /api/events/registrations/{id}/certificate` | Member (own) / Admin | Streams the generated PDF |

### 5. Frontend

- Real Events list/detail/register pages, replacing `EventsPreviewWidget.tsx`'s static mock per its
  own comment.
- Admin event roster screen: per-attendee payment/attendance/evaluation status, a "Set CPD units"
  action, and manual attendance override.
- Member "My CPD" page: credit history per event, running total, certificate download once earned.

### 6. Certificate generation

PDF generated on demand at request time, not stored. Library choice (e.g. `QuestPDF`, the closest
fit for a .NET 8 codebase with no existing PDF-generation code) is left open for the implementation
plan — not decided here, since it has no bearing on the data model or API shape.

## Error Handling / Edge Cases

- **Duplicate registration** for the same event by the same member is refused — one
  `EventRegistration` per member per event, mirroring the existing "one pending payment at a time"
  rule.
- **Rejected payment** leaves the registration in `Rejected`; the member can resubmit, same as a
  rejected membership payment today.
- **Evaluation submitted before `Attended`** is refused — completion cannot precede attendance.
- **Certificate requested before the credit condition is true** (not yet `EvaluationSubmitted`, or
  `CpdUnits` still null) returns a clear "not yet available" response, not a broken or empty PDF.
- **Event cancellation** (and any resulting refund/notification flow) is explicitly out of scope for
  this pass — see Not Built.

## Not Built

- **CPD target/renewal-cycle tracking** — comparing earned units against a required threshold tied
  to `PrcValidUntilDate`. Needs the client's actual unit requirements before it can be designed.
- **Event cancellation, refunds, or capacity waitlisting** beyond a simple `Capacity` counter.
- **Chapter-officer-level event management permissions** — Admin/staff only for now.
- **Per-event configurable evaluation forms** — the evaluation is a fixed field set in this pass.
- **Any CPD credit from outside PSMPE-run events** — this feature only tracks credit earned through
  events this system manages, not member-self-reported external CPD activity.

## Impact

- Affected specs: `events` (**new** capability, delta spec in this folder), `payments` (**modified**
  — `Kind` gains an `EventRegistration` case)
- Affected code:
  - `Domain`: new `Event`, `EventRegistration` entities; `Payment.Kind` enum extended
  - `Application`: new event/registration/CPD DTOs and service methods; `PaymentService.VerifyAsync`
    extended to drive `EventRegistration.Status`
  - `Infrastructure`: EF configurations + migration for the two new tables and the `Payment` FK
  - `WebAPI`: new `EventsController`; `PaymentsController`'s verify endpoint extended
  - `Web`: new Events pages, admin roster screen, member "My CPD" page;
    `EventsPreviewWidget.tsx` removed/replaced

## Open Questions For The Client

These were the user's best guesses during brainstorming, not confirmed requirements — flagging them
here so they get a real answer before (or during) implementation rather than being silently assumed:

1. **Are all PSMPE events actually paid?** This proposal routes every registration through the
   Payment flow (with `Fee` settable to 0 for a free event) rather than building a separate unpaid
   path. If chapter meetings or similar are routinely free with no proof-of-payment step at all,
   this needs a second registration pathway.
2. **What is the actual CPD unit requirement per renewal cycle**, and how does it map to
   `PrcValidUntilDate`? Needed before target/cycle tracking (currently deferred) can be built.
3. **Does event cancellation need to be supported** in the first version, including any refund
   handling for already-verified payments?
