# Change: Payment Verification Moves Into The Approval Wizard

## Status

**Implemented.** Raised by the user from the live UI ("the payment verification should be inside
this modal approval process instead"). Built and verified the same day: backend build clean,
**325 tests passing** (up from 322), frontend typecheck/lint/build clean. See `tasks.md` for what
remains unverified (the live browser pass).

## Why

Admitting a new applicant took three decisions in two places: RMP licence and Membership ID inside
the approval wizard, then a separate trip to the Payments tab to accept their money. Same friction
that drove RMP verification into the wizard in the first place.

Worse, the two were independent, so an admin could admit someone — issuing a control number, a
receipt and an email — while their payment sat unreviewed.

## The Deadlock This Had To Solve

The obvious implementation, "approval requires a verified payment", cannot work:

- `ApproveAsync` would require a verified payment
- `VerifyAsync` for a `NewMembership` payment requires `ApprovedAt` — it's what the first renewal
  date is computed from

Neither can go first. **Resolution: one transaction, correct order.** `ApproveAsync` assigns the
number, sets `ApprovedAt`, then applies the payment, in a single `SaveChangesAsync`. The policy
holds — no member is ever observably approved-but-unpaid — without either guard waiting on the
other.

## Decisions

Each resolved by the user during planning:

- **Couple them** — no admission without payment (the override variant was offered and declined).
- **Collect decisions, execute in order at Confirm** — rather than approving mid-wizard.
- **Let the admin record the payment in the wizard** when the member has none, rather than requiring
  payment details at profile creation.
- **Fix the seeder** so demo data obeys the rules.

## What Changes

- **`ApproveAsync` becomes the orchestrator**, taking `ApproveMemberRequest` (now
  `{ MembershipNo, Payment? }`) and the deciding user. `POST /api/members/{id}/approve` *is* that
  endpoint — deliberately not a second one alongside it, since a bare approve would be a loophole.
- **`PaymentVerification.Apply`** extracted so `VerifyAsync` (renewals) and `ApproveAsync`
  (registration) share one definition of the due-date arithmetic.
- **`POST /api/payments/member/{id}/proof`** (`members:manage`) stores a proof and returns its key.
  It deliberately does *not* create a `Payment` row — that happens inside the approving transaction,
  so a failed approval leaves an unreferenced file rather than an orphaned payment.
- **The wizard is four steps**: RMP Licence → Payment → Membership ID → Confirm. The payment step
  reviews an existing payment or records a new one, depending on what the member has.
- **The seeder** now produces members with a verified RMP licence and a verified registration
  payment.

## Findings

- **The seed data violated all three rules at once.** Every seeded member was approved and `Active`
  with **no licence number at all**, `PrcIdVerified = false`, and zero payments — which is why the
  wizard rendered blank RMP fields in the screenshot that prompted this. Seeders write entities
  directly, bypassing the services. Now fixed; the demo set reflects how the product actually works.
- **12 tests failed the moment the coupling landed**, all approvals with no payment — the change
  proving itself, and the bulk of the work.
- **Step indexes became named constants** (`STEP_RMP`/`STEP_PAYMENT`/`STEP_ID`/`STEP_CONFIRM`).
  Renumbering four steps by hand is exactly how an off-by-one gets into a flow that moves money.

## Accepted Trade-off

A review decision ("is this person a qualified Master Plumber?") now blocks on an accounting fact.
PSMPE can no longer admit someone whose cheque is still clearing. This was raised before
implementation and chosen deliberately over an override variant — but it's the thing to revisit if
that case turns out to be common in practice, and unwinding it means touching the same 16 test call
sites again.

## Not Changed

- **The Payments tab stays.** Renewals never touch this wizard, so it remains the only path for them.
- **`VerifyAsync`'s `ApprovedAt` guard** — untouched; the orchestrator calls `Apply` directly, after
  approval, inside the transaction.
- **Automatic `Status → Expired`** — still needs a scheduler that doesn't exist.
