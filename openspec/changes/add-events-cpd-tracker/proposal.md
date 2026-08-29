# Change: Event Management & CPD Credit Tracker

## Status

**Brainstormed, not yet approved for implementation.** Designed 2026-08-23 through collaborative
brainstorming with the user, working through scope one question at a time. Several answers are the
user's own best judgment standing in for the actual PSMPE client — each is called out explicitly
under "Open Questions For The Client" below and should be confirmed before implementation starts.
No code exists for any of this yet.

**Revised 2026-08-24** against a stakeholder interview transcript (PSMPE admin staff walking through
their real event/CPD workflow). The interview confirmed the core architecture below is sound, but
corrected several assumptions the original brainstorm got wrong: events run in two modalities at
once with independently accredited CPD units, attendance is per-lecture and prorates credit rather
than being a single whole-event flag, and most face-to-face attendees pay cash on-site rather than
through a proof-upload flow. See the updated Decisions, What Changes, and Not Built sections below.

**Revised 2026-08-29** against PRC's public "List of Accredited Programs" data for PSMPE's own
accredited events. That data showed the registration fee and the official PRC accreditation code
both vary by modality (not just CPD units, as the 2026-08-24 revision had assumed), and surfaced
several fields (`Type`, `Hours`, `Objectives`, a per-session `Venue`, a poster image) not previously
captured. See the updated Decisions and What Changes sections below.

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
- **Attendance = admin roster reconciliation after the event, not member self check-in.** PSMPE
  staff already reconcile a hard-copy PRC sign-in sheet against every event's attendee list as part
  of their existing compliance process; the portal mirrors that instead of inventing a separate
  self-check-in mechanism members would have to use mid-event. An Admin opens a per-event roster
  after the event and marks, per registrant, which sessions they attended. *Confirmed by stakeholder
  interview (2026-08-24), superseding the original self-check-in decision.*
- **Events span multiple sessions/lectures, and attendance is per-session.** A multi-day event with
  several lectures issues one certificate per attendee, but the certificate — and the CPD credit —
  reflects only the sessions actually attended, prorated from the event's total units (e.g. attended
  3 of 6 lectures on an 8-unit event earns a fraction of 8, not the full 8 and not zero). This
  matches PSMPE's actual certificate practice, including their existing "consideration" exception
  for members who leave early due to an emergency — they still get credited for the sessions they
  did attend, using the same prorating rule, not a special case. *Confirmed by stakeholder interview
  (2026-08-24) — the original brainstorm had no session concept at all.*
- **CPD units are not fixed when the event is created, and are tracked per modality.** Every PSMPE
  event runs face-to-face and via Zoom simultaneously, and each modality is accredited through its
  own separate CPDAS submission with its own approved unit count (Zoom typically ends up lower than
  face-to-face, but not by a fixed ratio PSMPE relies on — it's whatever each submission comes back
  approved for). `Event.CpdUnitsOnsite` and `Event.CpdUnitsOnline` are both independently nullable
  and shown as "TBD" until an admin sets them — which can happen before *or* after the event, since
  PSMPE's own CPDAS approval can take anywhere from under a week to two or three weeks, and the
  event proceeds on its own schedule regardless of where that approval stands. Registration,
  payment, attendance, and evaluation all proceed normally regardless of whether either value is set
  yet. *Revised 2026-08-24: originally a single `Event.CpdUnits` field; the stakeholder interview
  confirmed the two modalities need independent values, not a derived ratio.*
- **A registration records which modality the member attended (`Mode`: Onsite or Online).** This
  determines which of the two per-event unit values applies when computing that registration's
  credit.
- **CPD credit is computed at read time, never stored, and is prorated by attendance.** A
  registration's earned credit is `(sessions attended / total sessions) × (Event.CpdUnitsOnsite or
  Event.CpdUnitsOnline, based on the registration's Mode)`, counted only if that registration reached
  `EvaluationSubmitted` and the relevant modality's unit value is set. This still mirrors
  `Payment.IsExpired` (a computed property, not a background job — this codebase's only two
  `IHostedService`s, log retention and the newer membership-lifecycle scheduler, aren't a fit for
  per-registration computation like this). It also means a unit value set or corrected after the
  fact is instantly correct for every attendee, with no backfill step.
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
- **On-site cash payments can be recorded directly by an admin, without a proof upload.** Most
  face-to-face attendees pay cash at the venue rather than transferring money and uploading proof —
  only online/Zoom registrants realistically fit the existing proof-upload pattern. An admin can mark
  a registration's `Payment` as verified directly for a cash payer; it converges on the same
  `PaymentVerified` state as the proof-upload path, so nothing downstream needs to know which path was
  used. *Confirmed by stakeholder interview (2026-08-24).*
- **Bulk-onboarding PSMPE's ~2,000 existing members who don't yet have a portal account is out of
  scope for this proposal.** This design assumes every event registrant already has a `Member` +
  `ApplicationUser` account. How legacy, off-portal members get onboarded is a separate, unresolved
  problem — still being discussed with the client — and will be its own future proposal.
- **Certificate: a downloadable PDF, generated on demand**, only once the credit condition above is
  true (`EvaluationSubmitted` + the relevant modality's unit value set) — not pre-generated or stored
  ahead of time, so a corrected unit value never leaves a stale certificate in circulation. The PDF
  lists which sessions the member attended and the prorated CPD units earned, matching PSMPE's
  existing certificate practice of only listing the lectures a member actually attended.
- **Registration fee and the official PRC accreditation code are also tracked per modality**,
  mirroring `CpdUnitsOnsite`/`CpdUnitsOnline`. PRC's own accreditation data shows PSMPE routinely
  submits a single physical event as two separate accredited programs — one Onsite, one Online —
  each with its own approved fee and its own PRC-assigned code (e.g. one real event: ₱900/4.00 units
  online vs ₱3,000/8.00 units onsite, two different codes). *Revised 2026-08-29 against PRC's public
  data — flagged under Open Questions as not yet confirmed with PSMPE staff directly.*
- **No `Category` field.** Every PRC listing for PSMPE shows the same category ("Master Plumbing")
  because PSMPE is a single-profession organization — not a value PSMPE staff would ever set when
  creating an event in this portal. Skipped entirely rather than built as a field nobody will change.
- **Event eligibility is always open.** PRC's listings show "Open to All" for every PSMPE program;
  there is no restriction logic to build — any authenticated member can register for any event.
- **`Event.Capacity` is an informational target, not an enforced cap.** PRC's "target no. of
  participants" reads as a planning figure PSMPE tracks for its own accreditation submission, not a
  hard limit — reaching it never blocks a new registration. This supersedes any reading of the
  original Capacity decision as an enforced counter.
- **Three new informational fields on `Event`:** `Type` (free text against a constants list — e.g.
  Conference, Seminar, Technoforum, Convention, Symposium, Expo — mirroring the existing
  `MemberTypes` pattern), `Hours` (a single decimal, shared across both modalities — PRC's data shows
  the same declared hour count regardless of modality), and `Objectives` (long text, same shape as
  `Description`). None of these drive any behavior; they exist for display and for the certificate.
- **`EventSession` gains an optional `Venue` override.** PRC's per-event schedule table shows a
  Venue column per date/session row, implying a session's venue can differ from the event's default
  (e.g. a multi-city or multi-room event) — falls back to `Event.Venue` when not set.
- **Event poster/banner image upload.** An Admin can attach an image when creating or editing an
  event, reusing the existing upload infrastructure rather than building new storage/validation.
  Displayed on the event detail page and as the banner on member-facing Events list/register pages.
- **The Events list, admin roster, and any review queue support search and filter**, not just
  sorting — this project's standing convention for every list/table, reinforced by PRC's own listing
  having a built-in search box.

## What Changes

### 1. New `Event` entity (Domain)

`Title`, `Description`, `Objectives` (nullable text), `Type` (nullable string, free text against a
constants list — Conference, Seminar, Technoforum, Convention, Symposium, Expo), `Chapter`, `Venue`,
`StartsAt`, `EndsAt`, `Hours` (nullable decimal), `Capacity` (int — informational target, does not
block registration), `FeeOnsite` (decimal, settable to 0 for a free event), `FeeOnline` (decimal,
settable to 0, independent of `FeeOnsite`), `CpdUnitsOnsite` (nullable decimal — null means "TBD"),
`CpdUnitsOnline` (nullable decimal — null means "TBD", independent of `CpdUnitsOnsite`),
`CpdCodeOnsite` (nullable string — PRC's own accreditation reference for the onsite program,
informational only, not validated against PRC), `CpdCodeOnline` (nullable string, same for the
online program), `PosterImageStorageKey` (nullable string — same shape as `MemberUpload`'s
`StorageKey`).

### 2. New `EventSession` entity (Domain)

FK to `Event`. `Title`, `StartsAt`, `EndsAt`, `Order` (int, for display sequencing), `Venue`
(nullable string — overrides `Event.Venue` for this session when set, falls back to it otherwise).
Represents one lecture/segment of a (possibly multi-day) event — the unit attendance is actually
tracked against. An event with no separate lectures still gets exactly one `EventSession` covering
the whole event, so the attendance/credit model below doesn't need a special case for
single-session events.

### 3. New `EventRegistration` entity (Domain)

FKs to `Member` and `Event`. `Mode` enum: `Onsite`, `Online` — chosen at registration, determines
which of `Event.CpdUnitsOnsite` / `Event.CpdUnitsOnline` applies to this registration's credit.
`Status` enum: `Registered`, `PaymentSubmitted`, `PaymentVerified`, `Attended`,
`EvaluationSubmitted`, `Rejected`, `Cancelled`. Evaluation fields captured directly on the row:
`EvaluationRating`, `EvaluationComments`, `EvaluationSubmittedAt`.

### 4. New `EventAttendance` entity (Domain)

Join row: FKs to `EventRegistration` and `EventSession`, plus `RecordedBy` (the admin who reconciled
it) and `RecordedAt`. One row per session a registrant is confirmed to have attended. This is what
"attended" now means structurally — replacing the single `AttendedAt`/`AttendedBy` flag from the
original design. `EventRegistration.Status` moves to `Attended` once an admin records the first
`EventAttendance` row for that registration during roster reconciliation.

### 5. Payment integration

`Payment.Kind` gains an `EventRegistration` case; `Payment` gains a nullable
`EventRegistrationId` FK (mirrors how `Payment.MemberId` already works). The existing
`POST /api/payments/{id}/verify` endpoint is extended: when `Kind == EventRegistration`, verifying
advances the linked `EventRegistration.Status` from `PaymentSubmitted` to `PaymentVerified`, the
same way verifying a membership payment today flips `Member.Status` to `Active`. A new admin-only
action records a cash payment directly — creating and verifying a `Payment` in one step, with no
proof file — for on-site registrants who paid in cash at the venue. The amount owed for a
registration resolves from `Event.FeeOnsite` or `Event.FeeOnline` based on the registration's
`Mode`, not a single shared `Event.Fee`.

### 6. API endpoints

| Endpoint | Role | Purpose |
|---|---|---|
| `GET /api/events` | Any authenticated | List events (upcoming/past), with search + filter query params |
| `POST /api/events` | Admin | Create event (`CpdUnitsOnsite`/`CpdUnitsOnline` start null) |
| `PUT /api/events/{id}` | Admin | Edit event details, including setting/correcting either CPD unit value; manage `EventSession`s |
| `POST /api/events/{id}/register` | Member | Create `EventRegistration` with a chosen `Mode` (→ `Registered`) |
| `POST /api/events/registrations/{id}/payment` | Member | Submit proof (reuses payment-submit pattern) |
| `POST /api/events/registrations/{id}/payment/cash` | Admin | Record a cash payment directly (→ `PaymentVerified`, no proof) |
| `POST /api/events/{id}/roster/attendance` | Admin | Bulk-record which sessions each registrant attended (roster reconciliation, → `Attended`) |
| `POST /api/events/registrations/{id}/evaluation` | Member | → `EvaluationSubmitted` |
| `GET /api/events/{id}/roster` | Admin | Full attendee list with per-session attendance, payment, and evaluation status; search + filter query params |
| `GET /api/members/me/cpd` | Member | Own registrations plus computed, prorated credit total |
| `GET /api/events/registrations/{id}/certificate` | Member (own) / Admin | Streams the generated PDF |

### 7. Frontend

- Real Events list/detail/register pages, replacing `EventsPreviewWidget.tsx`'s static mock per its
  own comment. Registration includes choosing Onsite or Online, and shows the fee and CPD units for
  the selected modality. The Events list supports search + filter. The event detail page shows the
  poster image, `Type`, `Hours`, `Objectives`, and each session's `Venue` (or the event's default).
- Admin event roster screen: per-attendee payment status (including a cash-payment action),
  per-session attendance checkboxes for reconciliation, evaluation status, and a "Set CPD units"
  action for each modality.
- Member "My CPD" page: credit history per event (with modality and sessions attended), running
  total, certificate download once earned.

### 8. Certificate generation

PDF generated on demand at request time, not stored, listing the sessions attended and the prorated
credit earned. Library choice (e.g. `QuestPDF`, the closest fit for a .NET 8 codebase with no
existing PDF-generation code) is left open for the implementation plan — not decided here, since it
has no bearing on the data model or API shape.

## Error Handling / Edge Cases

- **Duplicate registration** for the same event by the same member is refused — one
  `EventRegistration` per member per event, mirroring the existing "one pending payment at a time"
  rule.
- **Rejected payment** leaves the registration in `Rejected`; the member can resubmit, same as a
  rejected membership payment today.
- **Evaluation submitted before `Attended`** is refused — completion cannot precede attendance.
- **Certificate requested before the credit condition is true** (not yet `EvaluationSubmitted`, or
  the relevant modality's unit value still null) returns a clear "not yet available" response, not a
  broken or empty PDF.
- **Attendance recorded for a session that doesn't belong to the event** is refused — an
  `EventAttendance` row must reference an `EventSession` of the same `Event` as the registration.
- **Cash payment recorded for a registration that already has a submitted proof payment** is refused
  — a registration has exactly one `Payment`, regardless of which path (proof upload or admin cash
  entry) reaches `PaymentVerified`.
- **Event cancellation** (and any resulting refund/notification flow) is explicitly out of scope for
  this pass — see Not Built.

## Not Built

- **CPD target/renewal-cycle tracking** — comparing earned units against a required threshold tied
  to `PrcValidUntilDate`. Needs the client's actual unit requirements before it can be designed.
- **Event cancellation, refunds, or capacity waitlisting.** `Capacity` is an informational target
  only in this pass — it is never enforced, so there is nothing to waitlist against. *Revised
  2026-08-29: corrects the original framing, which implied `Capacity` was an enforced counter.*
- **Chapter-officer-level event management permissions** — Admin/staff only for now.
- **Per-event configurable evaluation forms** — the evaluation is a fixed field set in this pass.
- **Any CPD credit from outside PSMPE-run events** — this feature only tracks credit earned through
  events this system manages, not member-self-reported external CPD activity.
- **CPDAS/PRC submission integration** — PSMPE applies for CPD accreditation on PRC's CPDAS platform
  directly, outside this system entirely. This feature never talks to CPDAS/PRC; it only records
  whatever unit values an admin enters once PSMPE's own accreditation process resolves.
  *Confirmed out of scope by stakeholder interview (2026-08-24).*
- **Hard-copy PRC attendance sheet digitization** — PSMPE is required to keep a physical,
  PRC-formatted sign-in sheet as part of their submission package; this system does not scan, upload,
  or replace that document. Admin roster reconciliation in the portal is a separate, lighter-weight
  record for the portal's own purposes. *Confirmed out of scope by stakeholder interview
  (2026-08-24).*
- **Completion report generation** — the PDF package (completion form, attendance sheet, lecturer
  profiles, lecture materials) PSMPE submits to PRC after each event is assembled entirely outside
  this system. *Confirmed out of scope by stakeholder interview (2026-08-24).*
- **Bulk onboarding of existing, off-portal members** (PSMPE's ~2,000-member legacy list) — every
  registrant in this design already has a `Member` + `ApplicationUser` account. How legacy members
  get imported or invited is unresolved and deferred to a future proposal.

## Impact

- Affected specs: `events` (**new** capability, delta spec in this folder), `payments` (**modified**
  — `Kind` gains an `EventRegistration` case)
- Affected code:
  - `Domain`: new `Event`, `EventSession`, `EventRegistration`, `EventAttendance` entities;
    `Payment.Kind` enum extended
  - `Application`: new event/session/registration/attendance/CPD DTOs and service methods;
    `PaymentService.VerifyAsync` extended to drive `EventRegistration.Status`; new cash-payment
    service method
  - `Infrastructure`: EF configurations + migration for the four new tables and the `Payment` FK
  - `WebAPI`: new `EventsController` (with search/filter query params on the list and roster
    endpoints); `PaymentsController`'s verify endpoint extended
  - `Web`: new Events pages (with search/filter, poster image, modality-aware fee/units display),
    admin roster screen (with per-session reconciliation and cash-payment action), member "My CPD"
    page; `EventsPreviewWidget.tsx` removed/replaced

## Open Questions For The Client

These were the user's best guesses during brainstorming, not confirmed requirements — flagging them
here so they get a real answer before (or during) implementation rather than being silently assumed:

1. **Are all PSMPE events actually paid?** This proposal routes every registration through the
   Payment flow (with `FeeOnsite`/`FeeOnline` each settable to 0 for a free modality) rather than
   building a separate unpaid path. If chapter meetings or similar are routinely free with no
   proof-of-payment step at all, this needs a second registration pathway.
2. **What is the actual CPD unit requirement per renewal cycle**, and how does it map to
   `PrcValidUntilDate`? Needed before target/cycle tracking (currently deferred) can be built.
3. **Does event cancellation need to be supported** in the first version, including any refund
   handling for already-verified payments?
4. **What audit trail does an admin-recorded cash payment need?** This proposal has admin marking a
   registration paid directly with no proof file — does that need a receipt/reference number field,
   or is the existing `Payment` audit trail (who verified it, when) sufficient for PSMPE's
   bookkeeping?
5. **How should PSMPE's ~2,000 existing, off-portal members be onboarded?** Raised in the stakeholder
   interview but not resolved (discussion was interrupted mid-call) — still being discussed with the
   client. Out of scope for this proposal; will need its own future proposal covering bulk
   import/invitation of legacy members who don't yet have a portal account.
6. **Does the Fee/accreditation-code-per-modality split actually match PSMPE's internal process?**
   This was the user's own reading of PRC's *public* accreditation data, not something confirmed with
   PSMPE staff directly (unlike the CPD-units modality split, which the 2026-08-24 stakeholder
   interview did confirm). Needs a direct confirmation before implementation: does PSMPE genuinely
   charge and account for Onsite/Online registrations as separately-priced, separately-coded programs
   internally, or is the public PRC data simply reflecting two accreditation submissions for
   bookkeeping reasons that don't need to carry through to member-facing pricing in the portal?
