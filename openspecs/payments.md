# Payments

## Purpose

Membership payments — the one-time fee at registration and annual renewal dues. Members declare
what they paid and attach proof; an admin verifies or rejects it.

**Verifying a payment sets `MembershipStatus.Active` and moves `RenewalDueDate`; the daily
`MembershipLifecycleService` job is the only other thing that changes `Status`, flipping a lapsed
member to `Expired`.** Both used to be manual admin edits on the member record, which `members.md`
recorded as a deferred gap: *"Once a Payments domain exists, it should be the thing that flips
`Status`, not manual admin edits."*

Renewals did not exist at all before this: no member-facing UI, nothing advanced the due date, and
`MembershipStatus.Expired` was never set by anything. See "Membership lifecycle" below for the
reminder emails, grace period, and auto-expiry now built on top of it.

## Endpoints

All under `/api/payments`, all authenticated.

- `GET /` — admin queue. `members:view`. Query: `page`, `pageSize`, `status` (defaults to
  `Submitted`, the only status needing action). Ordered **oldest first** — a queue is worked front
  to back.
- `GET /me` — the caller's own history, including rejected payments and their reasons. Returns an
  empty array (not 404) for a caller with no membership profile.
- `GET /member/{memberId}` — one member's history. `members:view`.
- `POST /me` — self-service submission. `{ amount, referenceNo?, paidOn }`.
  - `400` if amount ≤ 0, `paidOn` is in the future, or the reference exceeds 64 chars.
  - `409` if a payment is already awaiting verification — see "One at a time" below.
  - **`Kind` is derived, never taken from the caller** (below).
- `POST /{id}/proof` — attaches the deposit slip / screenshot to the caller's **own** pending
  payment. Multipart, separate from `POST /me` for the same reason the member document uploads are.
  `403` for someone else's payment; refused once the payment has been decided.
- `GET /{id}/proof` — serves the file. The owner, or staff with `members:view`.
- `POST /{id}/verify` — `members:manage`. Idempotent.
- `POST /{id}/reject` — `members:manage`. `{ reason }`, required.
- `GET /fees` — **any authenticated caller**: the registration wizard shows the total to an
  applicant who holds no permissions yet.
- `PUT /fees` — `members:manage`.
- `GET /fees/promotions`, `POST /fees/promotions`, `DELETE /fees/promotions/{id}` — `members:manage`.
  See "Promotional pricing" below.
- `GET /reports/summary?startDate=&endDate=` — `members:view`. See "Payment reporting" below.

## Approval and payment are one act

A member is **never** admitted without their registration payment being accepted in the same
transaction. `MemberService.ApproveAsync` does both: assigns the control number, sets `ApprovedAt`,
then applies the payment — one `SaveChangesAsync`, so there is no observable state in which someone
is approved but unpaid.

**Why it isn't a precondition.** "Approval requires a verified payment" and "verifying a
`NewMembership` payment requires `ApprovedAt`" (it's what the renewal date is computed from) is a
deadlock — neither can go first. Doing both in one transaction, in that order, satisfies the policy
without one guard waiting on the other.

- **The wizard is the only approval path.** `POST /api/members/{id}/approve` *is* the orchestrator;
  there is no bare approve endpoint that skips payment, because that would be a loophole around the
  policy.
- **`ApproveMemberRequest.Payment` handles the member with nothing on record.** A self-service
  applicant already has a payment (created at submit) and the admin only reviews it, so the block
  stays null. An admin-created profile has none — `POST /api/members` never creates one — so the
  approving admin records what the walk-in actually paid. Without this, admin-created members would
  be permanently unapprovable, the same deadlock the RMP licence rule hit.
- **Supplying a payment when one already exists is rejected, not ignored.** Silently discarding what
  the admin typed would let them believe they had corrected an amount that never changed.
- **A rejected registration payment blocks approval** until the member submits a new one.
- **Proof is uploaded first, referenced by key.** `POST /api/payments/member/{id}/proof`
  (`members:manage`) stores the file and returns its key; the approval request carries the key. The
  `Payment` row is only created inside the approving transaction, so a failed approval leaves an
  unreferenced file rather than an orphaned payment record.
- **`PaymentVerification.Apply`** holds the effect of accepting a payment — status, decider,
  `CoversUntil`, and the member's `Status`/`RenewalDueDate`. Both `PaymentService.VerifyAsync` (the
  standalone path, i.e. renewals) and `ApproveAsync` call it, so the due-date arithmetic — the one
  calculation here nobody can eyeball — has exactly one definition.

**Accepted trade-off:** a review decision ("is this person a qualified Master Plumber?") now blocks
on an accounting fact. PSMPE cannot admit someone whose cheque is still clearing. Chosen
deliberately over an override variant; revisit if that case turns out to be common.

## What verification does

| Kind | Effect |
|---|---|
| `NewMembership` | `Status = Active`, `RenewalDueDate = ApprovedAt + 1 year`, `Member.HasPortalAccess = payment.IncludesPortalAccess` |
| `Renewal` | `Status = Active`, `RenewalDueDate = previous RenewalDueDate + 1 year`, `Member.HasPortalAccess = payment.IncludesPortalAccess` |

Both record `CoversUntil` on the payment, so the history says not just that money was accepted but
what period it bought.

- **`Member.HasPortalAccess` is written exclusively here**, inside `PaymentVerification.Apply`. No
  admin action grants or denies it independently of what was actually paid for — verifying a
  payment and settling portal access happen in the same transaction as the same statement, with no
  separate "grant portal access" endpoint or admin toggle to bypass it. See "Portal access is a
  per-payment add-on" below.

- **The anniversary is fixed.** A renewal advances from the *previous due date*, not from today.
  Advancing from today would hand every late payer the grace period for free and permanently shift
  their date each year.
- **Payment cannot admit someone.** Verification is refused if `ApprovedAt` is null — approval is a
  separate decision gated on RMP verification (see `members.md`), and paying doesn't bypass it.
- **Refused without proof.** There is nothing to verify against.
- **Idempotent.** A repeat verify returns success without advancing the due date a second time.
- **Rejection changes nothing about the membership.** `Status` and `RenewalDueDate` are untouched —
  a rejected renewal leaves the member exactly where they were, still owing. The reason is shown to
  them and they can submit again.
- **A verified payment cannot be rejected.** Reversing one would mean un-advancing a due date and
  possibly deactivating a live member; deliberately not an endpoint.

### Kind is derived, not declared

`SubmitAsync` decides: no `RenewalDueDate` yet → `NewMembership`, otherwise `Renewal`. A member
cannot claim a renewal for a membership that was never activated, nor a second "new membership"
payment once they are active.

### One at a time

A second submission is refused while one is awaiting a decision. Without that, a member could queue
several and an admin verifying two of them would advance the due date twice for one year's dues.

## Proof documents are payment-owned

`Payment.ProofStorageKey` holds the file's key directly. It is **not** a `MemberUpload` row.

`MemberUpload` is unique per `(UserId, Kind)`, so a renewal proof would repoint the single
`ProofOfPayment` slot and the registration proof would become unreachable through it. (The file
itself survives — `UploadAsync` never deletes the previous one and keys are timestamped — but
nothing would know where it was.) A payment record whose evidence can go missing is not a record.

`MemberUploadService.UploadPaymentProofAsync` shares the validation and image optimisation of the
normal upload path (1 MB cap, JPG/PNG/PDF) but writes no `MemberUpload` row. `OpenByKeyAsync` serves
it back.

The storage key embeds the member's surname, first name and birthdate, so it is **never** on
`PaymentDto` — clients get `hasProof: boolean`, and `GetProofKeyAsync` stays server-side.

`UploadKind.ProofOfPayment` remains for existing rows and is still what the registration wizard
uploads; `SubmitMyProfileAsync` copies that key onto the registration `Payment` it creates.

## Registration payments

`MemberService.SubmitMyProfileAsync` creates a `NewMembership` payment when an application is first
submitted, carrying over the Proof of Payment the wizard already required.

Done there rather than by changing the submit gate so an older client that never calls
`POST /api/payments/me` still produces something verifiable — otherwise an application could be
approved and then have no payment able to activate it. If the member already declared one through
the payments endpoint, that row is kept and only its proof is filled in. The amount defaults to the
configured registration total, since the member declared none on that path.

## Fees

Four `SystemConfig` keys — `MembershipFee` (1500), `MembershipShippingFee` (200), `AnnualDues`
(600), `PortalFee` (900) — read together through one cached accessor (`MembershipFeeKeys.All` now
has four entries), with the shipped constants as fallbacks so a database missing the rows behaves as
before rather than charging zero.

They drive the registration wizard's Payment Details totals, `ReceiptGenerator`, and the amount
pre-filled on a member's renewal form. `ReceiptGenerator` takes them as a parameter rather than
reading them itself, so it stays a pure renderer with no database dependency.

- **`/membership-fees`** (`members:manage`) is the first admin-editable configuration screen in the
  product. Now scoped to these four values (`PortalFee` joined the original three) rather than a
  general `SystemConfig` editor.
- **`UpdateFeesAsync` is the first write path to `SystemConfigs`** anywhere in the app. Every other
  consumer assumed the table was seed-only and TTL expiry was enough, so it evicts its own cache
  entry — a stale price for ten minutes after an edit would be worse than no cache. If a
  grace-period editor is ever added it must do the same.
- **`SystemConfigSeeder` now seeds per key.** It previously only ran when the whole table was empty,
  so any key added after the first deployment would never appear on an existing database. Each
  missing key is now filled independently and admin-edited values are left alone.

## Portal access is a per-payment add-on

`PortalFee` is a fourth fee alongside the other three, but it is never mandatory and never a global
mode. The board considered an admin-wide switch between "membership only" and "membership + portal"
pricing policies first, but a member mid-cycle under one policy wouldn't pick up the other until
their own next renewal — up to twelve months away, with no clean way to backfill the difference.
Instead every registration and every renewal independently offers portal access as an optional
tick-box, decided fresh each time by whoever is paying (the member, or the admin recording a
walk-in's actual payment).

Three fields carry this:

- **`Payment.IncludesPortalAccess`** (bool) — whether *this specific payment* declared the add-on.
- **`Payment.PortalFeeAmount`** (decimal) — the exact `PortalFee` amount in effect when this payment
  was created, resolved through `FeePromotionResolver` (see "Promotional pricing" below).
  Deliberately captured independently of whatever the payer's declared `Amount` actually was — the
  same way the other three fees are never validated against `Amount` (see "Not built") — so a later
  fee or promotion edit can never retroactively change what a historical payment's portal-revenue
  contribution was. It is zero whenever `IncludesPortalAccess` is false.
- **`Member.HasPortalAccess`** (bool) — the member's *current* access. Recurring, not permanent: it
  reflects only the most recently *verified* payment's `IncludesPortalAccess`, and is written
  exclusively by `PaymentVerification.Apply` (see "What verification does" above). A renewal that
  omits the add-on revokes access at that point, in the same call that would otherwise have granted
  it.

Three call sites create a `Payment` and each independently resolves and stamps
`PortalFeeAmount`/`IncludesPortalAccess`:

- **`PaymentService.SubmitAsync`** — self-service submission (`POST /api/payments/me`), covering
  both registration proof-submission and renewals. `SubmitPaymentRequest.IncludePortalAccess` sets
  `IncludesPortalAccess` directly; `PortalFeeAmount` is resolved via
  `FeePromotionResolver.ResolveCurrentAsync` when it's true, else zero.
- **`MemberService.EnsureRegistrationPaymentAsync`** — the registration wizard's fallback path,
  called from `SubmitMyProfileAsync` when the applicant hasn't already created a payment through
  `POST /api/payments/me`. Adds the resolved `PortalFee` onto the computed `Amount` when the
  applicant ticked the wizard's opt-in checkbox.
- **`MemberService.ResolveRegistrationPaymentAsync`** — the admin walk-in path, invoked from
  `ApproveAsync` via `RecordPaymentRequest.IncludePortalAccess`. This is also the paper-form
  registration path: an admin re-keys an offline application through the same member-creation
  screen and records the payment/portal choice at approval time, so an applicant without reliable
  internet or portal comfort is served the same way. Unlike the other two call sites there's no
  computed default `Amount` to add the fee onto here — the admin types the total directly — but
  `PortalFeeAmount` is still resolved and stamped independently, for the same reporting-accuracy
  reason.

## Fee edits are prospective only

Editing `/membership-fees` — now four fields, MembershipFee/ShippingFee/AnnualDues/PortalFee —
only affects payments created *after* the edit. Every payment captures its own
`Amount`/`IncludesPortalAccess`/`PortalFeeAmount` once, at creation, and nothing ever re-reads live
config for an existing row — the same principle behind "Amount validation against the configured
fee" already being a deliberate non-feature (see "Not built"): a `Payment` is a record of what was
charged at the time, not a live view of current pricing.

## Promotional pricing

The `FeePromotion` entity (`FeeKey`, `PromoAmount`, `StartDate`/`EndDate`, `CreatedByUserId`) lets
any of the four fees carry a temporary discounted price for a date range — a one-day discounted
membership fee during an outreach event was the motivating case. It's resolved live by
`FeePromotionResolver.ResolveAsync`/`ResolveCurrentAsync`, a pure date-range lookup with no caching
of its own and no background job: it starts and stops by itself because every fee read simply asks
"is today between `StartDate` and `EndDate`?" rather than something having to flip a value at
midnight.

- **Overlapping promotions for the same fee are rejected at creation**
  (`PaymentService.CreatePromotionAsync`), which keeps at most one row active per `FeeKey` per day —
  the resolver never has to pick among several matches, and the first match is always the only
  match.
- **Admin CRUD** via `POST/GET/DELETE /api/payments/fees/promotions` (`members:manage` — unlike
  `GET /fees`, this configuration surface isn't shown to an unapproved applicant, so it isn't
  `[AllowExpiredMember]`). Deletes are hard deletes: a `FeePromotion` is a lightweight scheduling
  record, not an audited transaction like `Payment`, and nothing downstream references one by `Id`
  once it's gone since payments created during its window already captured their own amount.
- **UI**: a Promotions panel on `/membership-fees`, with Status and Fee filters and a "Single day"
  convenience checkbox that sets `StartDate = EndDate`.
- A promotion covering today evicts the same fees cache entry an `UpdateFeesAsync` edit does, so it
  takes effect on the same up-to-ten-minutes cadence as a manual price change.

## Payment reporting

`GET /api/payments/reports/summary?startDate=&endDate=` (`members:view`) answers "how much portal
revenue and membership revenue came in over this range" for the board/admin, without a full
line-item export. It filters to `Verified` `NewMembership`/`Renewal` payments only —
`EventRegistration` is excluded as a separate revenue stream (see `openspecs/events.md`), and a
`Submitted` or `Rejected` payment isn't real revenue yet — with `PaidOn` falling in the inclusive
`[startDate, endDate]` range.

`PaymentReportSummaryDto` returns:

- Membership-only count/total (`IncludesPortalAccess == false`).
- Combined count/total (`IncludesPortalAccess == true`).
- Portal revenue — `PortalFeeAmount` summed explicitly over the combined subset, rather than over
  every matching payment, so the figure doesn't quietly depend on the (currently true, but not
  worth trusting blindly) invariant that a membership-only payment always has a zero
  `PortalFeeAmount`.

An inverted range (`startDate > endDate`) is rejected as a `Result.Failure` in the service layer
rather than at the controller, so the rule is unit-testable. Admin UI: `PaymentsSummaryPanel.tsx` on
the Payments tab, with a month quick-pick (this month / last month / last 3 / last 6 months / this
year) plus a custom date range, rendered above `PaymentsQueueTable.tsx`.

## Event registration payments (`Kind.EventRegistration`)

`Payment.Kind` gained a third case, `EventRegistration`, alongside `NewMembership`/`Renewal`, and
`Payment` gained a nullable `EventRegistrationId` FK (mirrors how `Payment.MemberId` already works)
— added for the Event Management & CPD Credit Tracker feature, see `openspecs/events.md`.

- **`POST /{id}/verify` and `POST /{id}/reject` now branch on `Kind`.** For an `EventRegistration`
  payment, verifying/rejecting drives the linked `EventRegistration.Status` instead of
  `MembershipStatus`/`RenewalDueDate` — verifying moves it to `PaymentVerified`, rejecting moves it
  to `Rejected` (the member can resubmit). `EventPaymentVerification.Apply`
  (`Application/Payments/EventPaymentVerification.cs`) is the `EventRegistration` counterpart to
  `PaymentVerification.Apply` above: same shape (marks the `Payment` verified, stamps
  `DecidedByUserId`/`DecidedAt`), but with none of `PaymentVerification.Apply`'s membership-specific
  `Member.ApprovedAt`/`RenewalDueDate` arithmetic, since none of that applies to an event payment.
  Both `PaymentService.VerifyAsync` and the event-only `RecordEventCashPaymentAsync` (below) call it,
  so "this event payment is now verified" has exactly one definition regardless of which path reached
  it — the same reasoning that gives `PaymentVerification.Apply` a single definition for memberships.
- **Two new endpoints exist under `/api/events/...`, not `/api/payments/...`**, for the two payment
  actions that are genuinely new and specific to events — not just `Kind`-branching on an existing
  one:
  - `POST /api/events/registrations/{id}/payment` — member proof submission, scoped to a
    registration id rather than a bare payment id.
  - `POST /api/events/registrations/{id}/payment/cash` — admin-only, records a cash payment for an
    on-site payer: creates and verifies a `Payment` in one call, with no proof file, reaching
    `PaymentVerified` directly. Refused if the registration already has a submitted or verified
    `Payment` — a registration has exactly one active `Payment` regardless of path.
  See `openspecs/events.md` ("The two payment paths") for the full detail — not duplicated here so
  this file doesn't become a second copy of that documentation.

## Membership lifecycle: reminders, grace period, and auto-expiry

The grace period is 7 days (`SystemConfig` key `MembershipGracePeriodDays`), after which a lapsed
`Active` member is auto-transitioned to `Expired`. `MembershipLifecycleService`
(`PSMPE.Portal.Infrastructure`), wrapped by `MembershipLifecycleBackgroundService` — a daily
`PeriodicTimer`, the second scheduled job in this codebase after `LogRetentionBackgroundService`,
same shape (runs once immediately on startup, then every 24h, its own DI scope per tick) — does two
things on every tick:

- **Sends renewal reminder emails** at fixed points: 30 days before `RenewalDueDate`, 7 days
  before, on the due date itself, and once on the first day of the grace period (a single email,
  not a daily repeat throughout the window). Idempotent via `RenewalReminderLog`
  (`MemberId`/`ReminderType`/`ForRenewalDueDate`, unique-indexed) — keying on the due date the
  reminder was sent *for*, not the date it was sent *on*, is what lets reminders fire again
  automatically each renewal cycle with no cleanup job. A failed send for one member never blocks
  the rest of the run.
- **Auto-flips `Status: Active → Expired`** for every member whose `RenewalDueDate` plus the grace
  period has passed, as a single bulk `ExecuteUpdateAsync` statement — not a per-row loop, so it
  scales with membership size regardless of how many members lapse on a given day.

`MemberDto.IsExpired`/`IsInGracePeriod` stay derived from `RenewalDueDate` (not read from `Status`)
so they're accurate in the hours between ticks, not just after the daily job runs; they exclude
`Deactivated` members, since deactivation is a distinct admin action, not a lapsed-payment state.
`ComputeIsExpired`/`ComputeIsInGracePeriod` in `MemberService` are the single implementation both
the member-facing DTO and (indirectly, via the same grace-period config) the background job rely
on.

**Past the grace period, a member's portal access is restricted** to an explicit allowlist of
self-service endpoints — see `members.md`'s Authorization rules for the full mechanism
(`MembershipAccessMiddleware`, `[AllowExpiredMember]`). Paying a renewal (`POST /api/payments/me`
→ `POST /{id}/verify`) is itself always reachable, since `PaymentVerification.Apply` unconditionally
sets `Status = Active` on every verify — a member flipped to `Expired` by the nightly job is
restored to full access the moment their payment is verified, whether that happens the same day or
months later.

**A second, independent condition restricts access the same way**: an `Active` member whose
`HasPortalAccess` is `false` — i.e. their most recently verified payment didn't include the add-on —
is 403'd with `PORTAL_ACCESS_REQUIRED`, same allowlist and same JSON shape (`{ code, message }`) as
`MEMBERSHIP_EXPIRED`. `Deactivated` members are excluded from this check, mirroring the existing
exclusion in `ComputeIsExpired`/`ComputeIsInGracePeriod` — deactivation is a distinct admin action,
not a lapsed-payment state, and shouldn't be newly affected by this feature. The two checks run in a
fixed order: the pre-existing `Status == Expired` check runs first, unchanged, so a member failing
both conditions sees `MEMBERSHIP_EXPIRED`, never `PORTAL_ACCESS_REQUIRED`.

## Admin UI

A fourth tab on the Members page: **Payments**, listing `Submitted` payments with member, kind,
amount, reference and paid-on, plus View proof / Verify / Reject.

Verify is disabled when a payment has no proof — verifying something nobody looked at is the mistake
the queue exists to prevent.

**Known invariant break:** the other three tabs are one `GET /api/members` query with different
filters. This one lists *payments*, so it has its own endpoint and its own table component. It stays
a tab because that preserves "one place for admin membership work" — the alternative is a fourth nav
entry, which is exactly what the consolidation change removed.

## Member UI

`RenewalPaymentCard` on `/profile`, shown once an application is approved (dues are meaningless
before that). It shows the due date and configured annual dues, warns during the grace period and
after expiry, and carries the submit form plus full payment history with rejection reasons.

The form appears within 60 days of the due date, during grace, after expiry, or when no due date is
set yet. Paying early is fine; showing it eleven months out would be noise.

## Not built

- **Online payment gateway.** Members upload proof of an out-of-band transfer.
- **Partial payments, refunds, invoices.** One payment covers one period.
- **Amount validation against the configured fee.** Under- and overpayments both happen; the admin
  sees the proof and the declared amount and decides.
- **Mid-cycle portal upgrade.** No standalone way to add portal access between renewal dates — only
  through a renewal payment that includes it.
- **Bulk import for paper-form registrants.** The admin walk-in path
  (`MemberService.ResolveRegistrationPaymentAsync`) handles one paper form at a time; no batch/CSV
  intake exists. Confirmed still acceptable at current volumes (~100 forms) during design.
