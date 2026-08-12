# Tasks: couple-payment-to-approval

**Goal:** Move payment verification into the approval wizard so admitting an applicant is one
decision in one place, and make it impossible to admit someone whose payment hasn't been accepted.

**Architecture:** `ApproveAsync` becomes an orchestrator that admits the member and accepts the
registration payment in a single transaction — resolving the deadlock that a plain "approval
requires verified payment" precondition would create. The wizard grows a payment step with two
modes: review an existing payment, or record one for a member who has none.

**Before starting:** read `proposal.md` in this folder.

---

## 1. Shared verification effect

- [x] `PaymentVerification.Apply` extracted — status, decider, `CoversUntil`, and the member's
      `Status`/`RenewalDueDate`, in one place.
- [x] `PaymentService.VerifyAsync` calls it, so renewals and registrations can't drift apart.

## 2. ApproveAsync as orchestrator

- [x] `ApproveMemberRequest` becomes `{ MembershipNo, Payment? }`; new `RecordPaymentRequest`.
- [x] Signature takes the request and `decidedByUserId` (needed for the payment audit trail).
- [x] `ResolveRegistrationPaymentAsync`: returns the existing payment, or creates one from supplied
      details. Refuses when — no payment and none supplied; a payment exists *and* details were
      supplied; the existing payment was rejected; the existing payment has no proof.
- [x] Order inside the transaction: RMP guard → ID validation → resolve payment → set `ApprovedAt`
      → `PaymentVerification.Apply` → single `SaveChangesAsync`.
- [x] Idempotency preserved: already-approved still short-circuits to success before any of it.

## 3. Admin proof upload

- [x] `POST /api/payments/member/{id}/proof` (`members:manage`) stores the file, returns the key,
      creates **no** `Payment` row.

## 4. Wizard

- [x] Four steps via named constants, not bare indexes.
- [x] Payment step, review mode: amount, reference, paid-on, expected total, View proof. Blocks
      Continue when the payment has no proof.
- [x] Payment step, entry mode: amount pre-filled from the configured registration total, reference,
      date, proof upload. Blocks Continue until a proof is attached and the amount is valid.
- [x] Confirm summarises the payment alongside the Membership ID.
- [x] A failed approval returns to the Membership ID step, where the correctable field is.

## 5. Seeder

- [x] Seeded members get a licence, `PrcIdVerified = true`, and a verified registration payment with
      `CoversUntil`, matching what `PaymentVerification.Apply` would have produced.

## 6. Tests

- [x] 16 approve call sites converted to supply a payment via an `ApproveWithPayment` helper.
- [x] New: approving with no payment on record and none supplied is rejected, leaving `ApprovedAt`
      null **and** the number unassigned.
- [x] New: approval accepts the payment in the same transaction — asserts `Active`, the derived
      `RenewalDueDate`, and that the payment is `Verified` with a matching `CoversUntil`.
- [x] New: supplying a payment when one already exists is rejected.

## 7. Docs

- [x] `openspecs/payments.md` — new "Approval and payment are one act" section.
- [x] `openspecs/members.md` — approve endpoint updated.
- [x] This change package.

## 8. Verification

- [x] `dotnet build src/PSMPE.Portal.sln` — 0 warnings, 0 errors. **Stop the dev API first.**
- [x] `dotnet test src/PSMPE.Portal.sln --no-build` — 325 passing, 0 failing.
- [x] `npx tsc -b --noEmit`, `npm run lint` (0 errors, 3 known warnings), `npm run build`.

### Not yet done — needs a running app and a browser

- [ ] **Self-service applicant**: submit an application, then approve — the payment step shows their
      submitted payment with a working View proof, and Confirm admits them as `Active` with a
      renewal date one year out.
- [ ] **Admin-created member**: create a profile, then approve — the payment step shows the entry
      form pre-filled with the registration total, upload a proof, and confirm the resulting payment
      is `Verified`.
- [ ] Continue is blocked on the payment step until a proof is attached.
- [ ] A payment with no proof blocks approval with a clear message.
- [ ] Rejecting the RMP licence at step 1 still ends the flow with nothing committed.
- [ ] The Payments tab still works standalone for a **renewal**.
- [ ] Re-seed and confirm the wizard no longer shows blank RMP fields.
- [ ] Responsive at 375 / 768 / 1280 — four stepper nodes must not overflow the modal on mobile.
