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
| `NewMembership` | `Status = Active`, `RenewalDueDate = ApprovedAt + 1 year` |
| `Renewal` | `Status = Active`, `RenewalDueDate = previous RenewalDueDate + 1 year` |

Both record `CoversUntil` on the payment, so the history says not just that money was accepted but
what period it bought.

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

Three `SystemConfig` keys — `MembershipFee` (1500), `MembershipShippingFee` (200), `AnnualDues`
(600) — read together through one cached accessor, with the shipped constants as fallbacks so a
database missing the rows behaves as before rather than charging zero.

They drive the registration wizard's Payment Details totals, `ReceiptGenerator`, and the amount
pre-filled on a member's renewal form. `ReceiptGenerator` takes them as a parameter rather than
reading them itself, so it stays a pure renderer with no database dependency.

- **`/membership-fees`** (`members:manage`) is the first admin-editable configuration screen in the
  product. Deliberately scoped to these three values rather than a general `SystemConfig` editor.
- **`UpdateFeesAsync` is the first write path to `SystemConfigs`** anywhere in the app. Every other
  consumer assumed the table was seed-only and TTL expiry was enough, so it evicts its own cache
  entry — a stale price for ten minutes after an edit would be worse than no cache. If a
  grace-period editor is ever added it must do the same.
- **`SystemConfigSeeder` now seeds per key.** It previously only ran when the whole table was empty,
  so any key added after the first deployment would never appear on an existing database. Each
  missing key is now filled independently and admin-edited values are left alone.

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
