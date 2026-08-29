# Events & CPD Credit Tracking

## Purpose

PSMPE events and workshops (national conventions, chapter seminars, technical workshops) that carry
PRC CPD (Continuing Professional Development) credit. Members register, pay, get marked as attended
by an admin reconciling the event's sign-in sheet, and submit a post-event evaluation; once that
whole loop closes, the portal computes how much CPD credit the registration earned.

**CPD credit is computed at read time, never stored.** There is no `CreditUnits` column anywhere —
every screen and the certificate PDF derive it fresh from `EventRegistration.Status`/`Mode`, the
event's `CpdUnitsOnsite`/`CpdUnitsOnline`, and how many of the event's sessions the registrant
attended (see "The CPD credit formula" below). This is what lets an admin correct a CPD unit count
weeks after an event and have every attendee's credit — and every certificate generated from that
point on — instantly reflect the correction, with no backfill job.

Replaces `EventsPreviewWidget.tsx`, the static hardcoded mock the Dashboard shipped with per its own
"replace this whole component once the real module ships" comment.

See also: `openspecs/payments.md` for the shared `Payment`/`PaymentService` mechanics this feature
reuses (proof upload, verify/reject, one-payment-at-a-time), and `openspecs/members.md` for how
`Member` (not `ApplicationUser`) is what an `EventRegistration` links to.

## Endpoints

All under `/api/events`, all authenticated. Payment verify/reject for an event registration's
`Payment` happen through the existing `/api/payments/{id}/verify` and `/{id}/reject` — see
`openspecs/payments.md`'s new section on the `Kind` extension.

**"Owner" in the Auth column** means the endpoint isn't gated by any permission claim — it checks,
server-side, that the caller's own `Member.UserId` matches the registration's `Member.UserId`
(`registration.Member.UserId != userId` → `403`), the same ownership pattern used throughout
`EventService`/`PaymentService` for these four registration-scoped actions. It has nothing to do
with roles or permissions; a Member acting on someone else's registration id gets `403` regardless
of any permission they hold.

| Endpoint | Auth | Purpose | Errors |
|---|---|---|---|
| `GET /api/events` | Any authenticated | Paged event list — `search`, `chapter`, `upcomingOnly` filters | — |
| `GET /api/events/{id}` | Any authenticated | One event's detail, including its sessions | `404` unknown event |
| `POST /api/events` | `events:manage` | Create an event (`CpdUnitsOnsite`/`CpdUnitsOnline` start null) | `400` invalid (blank title, `EndsAt` before `StartsAt`); `403` without the permission |
| `PUT /api/events/{id}` | `events:manage` | Edit event details, set/correct either CPD unit value, add/remove/reorder sessions | `404` unknown event; `400` invalid (no sessions left, `EndsAt` before `StartsAt`, a session id not belonging to this event); `409` removing a session that already has recorded attendance |
| `POST /api/events/{id}/poster` | `events:manage` | Upload/replace the event's poster/banner image (multipart) | `404` unknown event; `400` not a JPG/PNG, over 8 MB, or unreadable; `403` without the permission |
| `GET /api/events/{id}/poster` | Any authenticated | Stream the poster image | `404` unknown event or no poster uploaded yet |
| `POST /api/events/{id}/register` | Any authenticated (Member) | Create an `EventRegistration` with a chosen `Mode` (→ `Registered`) | `404` unknown event; `400` unrecognized `Mode`; `409` already holds a non-cancelled registration for this event |
| `POST /api/events/registrations/{id}/cancel` | Owner | Cancel own registration | `404` unknown registration; `403` not the owner; `400` can no longer be cancelled (payment already verified or beyond) |
| `POST /api/events/registrations/{id}/payment` | Owner | Submit proof of payment (reuses the membership proof-submit pattern) | `404` unknown registration; `403` not the owner; `400` not awaiting payment, or invalid amount/date; `409` a payment is already awaiting verification |
| `POST /api/events/registrations/{id}/payment/cash` | `events:manage` | Record a cash payment directly — creates and verifies a `Payment` in one call, no proof file | `404` unknown registration; `400` not awaiting payment (already verified, attended, evaluated, or cancelled), or amount not greater than zero; `409` registration already has a submitted or verified payment |
| `POST /api/events/{id}/roster/attendance` | `events:manage` | Bulk per-session attendance reconciliation for every registrant on the roster | `400` a registration doesn't belong to this event, hasn't reached `PaymentVerified` yet, or a session id doesn't belong to this event |
| `POST /api/events/registrations/{id}/evaluation` | Owner | Submit the post-event evaluation (→ `EvaluationSubmitted`) | `404` unknown registration; `403` not the owner; `400` registration hasn't reached `Attended` yet |
| `GET /api/events/{id}/roster` | `events:view` or `events:manage` | Full attendee list — per-session attendance, payment (incl. cash flag), evaluation state, computed credit | `404` unknown event |
| `GET /api/members/me/cpd` | Any authenticated (Member) | Own registrations plus computed, prorated credit total | — |
| `GET /api/events/registrations/{id}/certificate` | Owner, or `events:view`/`events:manage` | Streams the generated PDF; `[AllowExpiredMember]` — see "Certificate" below | `404` unknown registration; `403` not the owner and not staff; `400` credit not yet earned (not `EvaluationSubmitted`, or the applicable unit value still null) |

`events:view`/`events:manage` are seeded to Admin (both) and Manager (`events:view` only), matching
the `members:view`/`members:manage` pattern in `roles.md` — editable afterward via `/admin/roles`
like any other permission. Status codes above follow `EventsController.ToErrorActionResult`'s
mapping: `NotFound` → `404`, `Forbidden` → `403`, `Conflict` → `409`, everything else → `400`.

## The `Event` → `EventSession` → `EventAttendance` shape

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
- **`EventRegistration`** — one row per member per event (mirrors `Payment`'s single-row-with-
  status-enum shape). Carries `Mode` (`Onsite`/`Online`), `Status` (`Registered` →
  `PaymentSubmitted` → `PaymentVerified` → `Attended` → `EvaluationSubmitted`, plus `Rejected`/
  `Cancelled` off-ramps), and the evaluation fields (`EvaluationRating`, `EvaluationComments`,
  `EvaluationSubmittedAt`) directly on the row. There is deliberately no `AttendedAt`/`AttendedBy`
  flag here.
- **`EventAttendance`** — a join row: `EventRegistrationId` + `EventSessionId`, plus `RecordedBy`/
  `RecordedAt` (mirrors `Payment.DecidedByUserId`/`DecidedAt`). One row per session a registrant is
  confirmed to have attended.

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

**Why attendance is per-session, not per-event.** PSMPE's own certificate practice — including their
"consideration" exception for a member who leaves an event early due to an emergency — credits
exactly the sessions attended, not the whole event or nothing. A multi-day event with several
lectures issues one certificate per attendee, but the certificate and the CPD credit both reflect
only the sessions actually attended, prorated from the event's total units. Modeling attendance as a
single whole-event flag couldn't express that; modeling it as one `EventAttendance` row per attended
session can, without a special case for the partial-attendance path.

## Why attendance is admin roster reconciliation, not member self-check-in

PSMPE staff already reconcile a hard-copy PRC sign-in sheet against every event's attendee list as
part of their existing compliance process. The portal mirrors that instead of inventing a separate
self-check-in mechanism members would have to use mid-event: an Admin opens a per-event roster
*after* the event and marks, per registrant, which sessions they attended
(`POST /api/events/{id}/roster/attendance`, bulk — one call covers every registrant the admin has
worked through on the printed sheet). `RecordAttendanceAsync` replaces the full attended-session set
for each registrant it's called with (a re-run with a corrected set overwrites, it doesn't
accumulate), refuses a session that doesn't belong to the event being reconciled, and refuses
recording anything for a registration that hasn't reached `PaymentVerified` yet. Recording at least
one attended session moves the registration to `Attended`.

This system does **not** scan, upload, or replace PSMPE's own PRC-formatted sign-in sheet — the
roster here is a separate, lighter-weight record for the portal's own purposes (see "Not Built").

## The `Mode` split (Onsite / Online)

Every PSMPE event runs face-to-face and via Zoom simultaneously, and each modality is accredited
through its own separate CPDAS submission with its own approved unit count — Zoom typically ends up
lower than face-to-face, but not by a fixed ratio PSMPE relies on; it's whatever each submission
comes back approved for. `Event.CpdUnitsOnsite`/`CpdUnitsOnline` are therefore independently
nullable ("TBD" until an admin sets them, which can happen before *or* after the event — PSMPE's own
CPDAS approval can take under a week or two-to-three weeks, and the event proceeds on its own
schedule regardless). Registration, payment, attendance, and evaluation all function normally while
either value is still null; only credit computation and certificate generation wait on it.

A registration records which modality the member actually attended in as `Mode`, chosen at
registration time. `Mode` is what selects which of the two event-level unit values applies to that
registration's credit — see `CpdCredit.For` below. Two registrations on the same event, one Onsite
and one Online, can and do earn different credit even with identical attendance.

## The CPD credit formula

`Application/Events/CpdCredit.cs`, `CpdCredit.For(registration, event, sessionsAttended,
totalSessions)`:

```
credit = (Event.CpdUnitsOnsite or CpdUnitsOnline, by registration.Mode) × sessionsAttended / totalSessions
```

— but only when `registration.Status == EvaluationSubmitted` **and** the applicable modality's unit
value is not null; otherwise the method returns `null` (no credit, not zero — the distinction matters
for "TBD" display). Called wherever credit needs to be shown or summed: the roster, the My CPD
summary, and certificate generation — never persisted as a column.

**Worked example**, using the 8-unit Onsite event from `spec.md`/the implementation plan, but with a
partial-attendance fraction that also demonstrates rounding: a registration that attended 1 of the
event's 3 sessions and has submitted its evaluation:

```
credit = 8 × 1 / 3 = 2.666666...  →  2.67
```

The raw division is a `decimal` operation and, for attendance fractions that don't divide evenly,
can produce up to 28 decimal digits (as above). **This is a real behavior detail added during
implementation, not in the original plan text**: `CpdCredit.For` rounds the result to **2 decimal
places using `MidpointRounding.AwayFromZero`**, matching the `HasPrecision(6, 2)` already declared
on `CpdUnitsOnsite`/`CpdUnitsOnline` in `EventConfiguration` — the computed value never carries more
precision than the input unit values do. So 1 of 3 sessions on an 8-unit event is stored/displayed/
certified as **2.67**, not truncated or left at full decimal precision.

The evenly-divisible case works the same way with nothing to round: 3 of 6 sessions on the same
8-unit event computes as `8 × 3 / 6 = 4` exactly. Both cases are covered by
`CpdCreditTests.For_PartialAttendance_ReturnsProratedValue` (the evenly-divisible case) and
`CpdCreditTests.For_NonEvenlyDivisibleAttendance_RoundsToTwoDecimalPlaces` (the rounding case).

## The two payment paths

A registration reaches `PaymentVerified` one of two ways, and **always has exactly one active
`Payment`** regardless of which path was used — nothing downstream (attendance, evaluation, credit,
the certificate) needs to know or care which one it was:

- **Member proof upload** — `POST /api/events/registrations/{id}/payment` creates a `Payment`
  (`Kind = EventRegistration`) in `Submitted` status and moves the registration to
  `PaymentSubmitted`; an admin then verifies or rejects it through the existing
  `POST /api/payments/{id}/verify` / `/reject` (see `openspecs/payments.md`). Intended for
  online/Zoom registrants, who realistically fit the existing transfer-and-upload-proof pattern.
- **Admin cash recording** — `POST /api/events/registrations/{id}/payment/cash` creates *and*
  verifies a `Payment` in one call, with no proof file, moving the registration straight to
  `PaymentVerified`. For the on-site majority who pay cash at the venue rather than transferring
  money and uploading proof. Refused if the registration already has a submitted or verified
  `Payment` — a registration cannot end up with two.

`EventRosterEntryDto.PaymentIsCash` tells the roster UI which path a given registration took —
derived from `Payment.ProofStorageKey` being null (a proof payment always has one attached before it
can reach `Verified`), not a separately stored flag.

`EventPaymentVerification.Apply` (`Application/Payments/EventPaymentVerification.cs`) is the
`EventRegistration` counterpart to `PaymentVerification.Apply` — it moves the registration to
`PaymentVerified` and marks the payment verified, with none of the membership-specific
`Member.ApprovedAt`/`RenewalDueDate` arithmetic that `PaymentVerification.Apply` does, since none of
that applies to an event payment. Both `PaymentService.VerifyAsync` (the member-proof path) and
`PaymentService.RecordEventCashPaymentAsync` (the cash path) call it, so the effect of "this event
payment is now verified" has exactly one definition regardless of which path reached it.

Rejecting an event-registration payment sets the registration's `Status` to `Rejected` and touches
nothing else — the member can submit a new payment proof for the same registration afterward, same
as a rejected membership renewal today.

## Certificate

`GET /api/events/registrations/{id}/certificate` streams a PDF (`CertificatePdfGenerator`,
QuestPDF) generated fresh on every request — never pre-generated, stored, or cached. Refused (a
clear "not yet available" response, not a broken or empty file) unless the registration has reached
`EvaluationSubmitted` **and** the applicable modality's unit value is set — the same condition
`CpdCredit.For` uses to return a non-null value. The PDF lists only the sessions the registrant
actually attended and the prorated credit currently computed for the registration, so a unit value
corrected after the fact is reflected the next time the certificate is requested, never a stale
number from whenever it happened to be downloaded before.

The endpoint is reachable while the caller's membership is `Expired`
(`[AllowExpiredMember]`) — a member should still be able to retrieve proof of CPD credit they already
earned even after their membership has lapsed, mirroring `GET /api/members/me/cpd`'s own
expired-access carve-out. A caller may only fetch their own registration's certificate unless they
hold `events:view` or `events:manage`, checked server-side off the authenticated user's own claims
(`User.HasClaim`) — never from a client-supplied flag.

## Not built

Carried over from `add-events-cpd-tracker/proposal.md`'s own "Not Built" section, so a future reader
doesn't wonder whether these were simply forgotten:

- **CPD target/renewal-cycle tracking** — comparing earned units against a required threshold tied
  to `Member.PrcValidUntilDate`. Members and admins see a running total of units earned, not a
  "9 / 15 required this cycle" comparison. Deferred pending the client's actual per-cycle unit
  requirement.
- **Event cancellation, refunds, or capacity enforcement/waitlisting** — `Event.Capacity` is an
  informational planning target only; `EventService.RegisterAsync` never reads it, so reaching it
  never blocks a new registration.
- **Chapter-officer-level event management permissions** — Admin/staff only (`events:manage`) in this
  pass, even though `Member.ChapterPosition` exists and events are chapter-scoped.
- **Per-event configurable evaluation forms** — the evaluation is a fixed field set
  (`EvaluationRating` 1–5, `EvaluationComments`) for every event, not admin-configurable.
- **Any CPD credit from outside PSMPE-run events** — no member-self-reported external CPD activity.
- **CPDAS/PRC submission integration** — PSMPE applies for CPD accreditation on PRC's CPDAS platform
  directly, entirely outside this system. This feature never talks to CPDAS/PRC; it only records
  whatever unit values an admin enters once PSMPE's own accreditation process resolves.
- **Hard-copy PRC attendance sheet digitization** — the portal's roster reconciliation is a separate,
  lighter-weight record for the portal's own purposes, not a replacement for PSMPE's required
  physical, PRC-formatted sign-in sheet.
- **Completion report generation** — the PDF package (completion form, attendance sheet, lecturer
  profiles, lecture materials) PSMPE submits to PRC after each event is assembled entirely outside
  this system.
- **Bulk onboarding of PSMPE's ~2,000 existing, off-portal members.** Every registrant in this design
  already has a `Member` + `ApplicationUser` account; how legacy members get imported or invited is
  unresolved and deferred to a future proposal.
