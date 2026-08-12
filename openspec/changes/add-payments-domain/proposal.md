# Change: Payments Domain — Verification for New Membership and Renewals

## Status

**Implemented.** Raised by the user ("I missed the payment verification for new membership and
renewal payment"), designed through collaborative brainstorming with four scope questions answered
directly. Built and verified the same day: backend build clean, **322 tests passing** (up from 307),
frontend typecheck/lint/build clean. See `tasks.md` for what remains unverified (the live browser
pass).

## Why

A member uploaded one Proof of Payment at registration — **required to submit, and never verified by
anyone**. An admin flipped `Status` to `Active` by hand and typed a `RenewalDueDate` by hand.
Renewals did not exist at all: no member-facing UI, nothing advanced the due date, and
`MembershipStatus.Expired` was never set by anything. `IsInGracePeriod` was computed and acted on by
nothing.

`openspecs/members.md` already recorded this as deferred: *"Payments/Dues domain doesn't exist yet…
Once a Payments domain exists, it should be the thing that flips `Status`, not manual admin edits."*

## Decisions

Each resolved by the user during planning:

- **Design and build both flows**, since new-membership and renewal payments share ~80% of the
  machinery.
- **Member submits, admin verifies**, mirroring the RMP verification pattern.
- **A renewal advances the due date from the *previous due date***, so the anniversary is fixed and
  paying late doesn't quietly buy extra time.
- **Fees move into `SystemConfig`, admin-editable.**

## What Changes

- **New `Payment` entity** (`Kind`, `Amount`, `ReferenceNo`, `PaidOn`, `ProofStorageKey`, `Status`,
  `RejectedReason`, `DecidedBy`/`DecidedAt`, `CoversUntil`) + `AddPayments` migration.
- **`PaymentService`** — submit, attach proof, verify, reject, fees. Verifying is the only path to
  `Status = Active` or a moved `RenewalDueDate`.
- **`PaymentsController`** under `/api/payments`.
- **Fees in `SystemConfig`** with a `/membership-fees` admin screen, feeding the wizard totals, the
  receipt, and the renewal form's pre-filled amount.
- **Admin Payments tab** on the Members page; **`RenewalPaymentCard`** on the member's profile with
  the submit form and full payment history.
- **`MemberDto.IsExpired`**, computed.

## Findings That Shaped The Design

- **A plan assumption was wrong, and checking it changed the design.** The plan said re-uploading a
  proof *deletes* the previous file. It doesn't — `UploadAsync` never calls `DeleteAsync` (only
  `DeleteAllForUserAsync` does), and proof keys are timestamped. Only the **pointer** is single-slot.
  So `Payment.ProofStorageKey` recording the key at submission is a durable reference, and the
  design works with the existing upload machinery rather than around it.
- **`SystemConfigSeeder` only seeded an entirely empty table.** Any key added after the first
  deployment would never have appeared on an existing database — the three fee keys included. Now
  seeds per key, leaving admin-edited values alone.
- **`UpdateFeesAsync` is the first write path to `SystemConfigs` in the product.** Every other
  consumer assumed seed-only and TTL expiry; a stale price for ten minutes after an edit would be
  worse than no cache, so it evicts its own entry. The now-incorrect "no write path anywhere"
  comment on the grace-period cache was corrected to say so.
- **The storage key embeds the member's surname, first name and birthdate**, so it is never on
  `PaymentDto` — clients get `hasProof`, and the key is fetched server-side only.
- **No scheduler exists anywhere** (no `IHostedService`), so automatic `Active → Expired` has nothing
  to run on. `IsExpired` is computed instead of faking it with a write-on-read that would make a GET
  mutate data.

## Design Notes

- **`Kind` is derived from the member's state**, never taken from the caller — otherwise a member
  could claim a renewal for a membership that was never activated.
- **One pending payment at a time.** Two would let an admin verify both and advance the due date
  twice for one year's dues.
- **Verification refuses an unapproved member.** Paying does not bypass the approval gate, which
  itself gates on RMP verification.
- **Registration payments are created by `SubmitMyProfileAsync`**, carrying over the proof the wizard
  already required, rather than by changing the submit gate. An older client that never calls the
  payments endpoint still produces something verifiable — without it an application could be
  approved and then have no payment able to activate it.
- **Rejection leaves the membership untouched** and a verified payment can't be rejected — reversing
  one would mean un-advancing a due date and possibly deactivating a live member.
- **`Payment.MemberId` is a `Restrict` FK** like `PrcVerificationHistory`, and `DeleteAsync`
  pre-checks so it surfaces as a clean failure rather than a raw `DbUpdateException`.

## Known Invariant Break

The Members page's other three tabs are one `GET /api/members` query with different filters. The
Payments tab lists *payments*, so it has its own endpoint and its own table. It stays a tab because
that preserves "one place for admin membership work" — the alternative is a fourth nav entry, which
is exactly what the consolidation change removed.

## Not Built

- **Automatic `Status → Expired`** — needs a scheduler.
- **Online payment gateway.** Members upload proof of an out-of-band transfer.
- **Partial payments, refunds, invoices.** One payment covers one period.
- **Amount validation against the configured fee.** Under- and overpayments both happen; the admin
  sees the proof and the declared amount and decides.
- **The info-icon tooltips** on the queue tabs, still pending from earlier.
