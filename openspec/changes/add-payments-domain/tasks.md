# Tasks: add-payments-domain

**Goal:** Make payment verification real — for the registration fee and for annual renewals — so
that verifying a payment, not an admin editing a record by hand, is what activates a membership and
moves its renewal date.

**Architecture:** A new `Payment` entity owning its own proof document, a `PaymentService` holding
all the due-date arithmetic, a `/api/payments` controller, fees in `SystemConfig`, an admin queue
tab, and a member-facing renewal card.

**Tech Stack:** .NET 8 + EF Core + Postgres, React 19 + Vite + TypeScript. No test runner in
`apps/web`; the backend has xUnit unit and integration projects.

**Before starting:** read `proposal.md` in this folder.

---

## 1. Domain and persistence

- [x] `Payment` entity, `PaymentKind` (NewMembership/Renewal), `PaymentStatus`
      (Submitted/Verified/Rejected).
- [x] `PaymentConfiguration`: `decimal(12,2)` amount, enums stored as **text** (matching
      `MemberUploadConfiguration`'s reasoning about shifting ordinals), indexes on `Status` and
      `MemberId`, `Restrict` FK.
- [x] `DbSet` on `ApplicationDbContext`, `IApplicationDbContext` and the unit-test context.
- [x] `AddPayments` migration — one table, no drift.
- [x] `MemberService.DeleteAsync` pre-checks payment history so the `Restrict` FK surfaces cleanly.

## 2. Fees in configuration

- [x] `MembershipFeeKeys` with the three keys and the shipped constants as fallbacks.
- [x] `SystemConfigSeeder` seeds **per key** — it previously only ran on a wholly empty table, so
      new keys would never appear on an existing database.
- [x] `PaymentService.GetFeesAsync` (cached) / `UpdateFeesAsync` (evicts, being the first write path
      to `SystemConfigs` in the product).
- [x] `ReceiptGenerator` takes fees as a parameter — stays a pure renderer with no DB dependency.
- [x] Corrected the grace-period cache comment that claimed no write path to `SystemConfigs` existed.

## 3. Service logic

- [x] `SubmitAsync` — derives `Kind` from member state, refuses a second pending payment, validates
      amount/date/reference.
- [x] `VerifyAsync` — NewMembership sets `ApprovedAt + 1 year`; Renewal advances from the **previous
      due date**; both set `Active` and record `CoversUntil`. Idempotent. Refuses an unapproved
      member and a payment with no proof.
- [x] `RejectAsync` — records the reason, leaves `Status`/`RenewalDueDate` untouched, refuses to
      reject an already-verified payment.
- [x] `MemberService.SubmitMyProfileAsync` creates the registration payment from the uploaded proof.
- [x] `MemberDto.IsExpired`, computed alongside `IsInGracePeriod`.

## 4. Proof documents

- [x] Extracted `StoreFileAsync` from `UploadAsync` so validation and image optimisation are shared.
- [x] `UploadPaymentProofAsync` returns the key and writes **no** `MemberUpload` row.
- [x] `OpenByKeyAsync` serves a payment-owned file.
- [x] The key stays server-side — `PaymentDto` exposes only `hasProof`.

## 5. API

- [x] `PaymentsController`: admin queue, own history, member history, submit, upload proof, serve
      proof (owner or `members:view`), verify, reject, get/update fees.

## 6. Frontend

- [x] `paymentApi` with blob-fetched proof URLs (the authenticated request a plain `<img src>` can't
      make).
- [x] `PaymentsQueueTable` — Verify disabled without proof, reject requires a reason, proof preview.
- [x] Payments tab on `MembersPage`, with its own fetch, its own count badge, and a `TabKey` type
      acknowledging it isn't a `MembersView`.
- [x] `RenewalPaymentCard` on `/profile` — due date, grace/expiry warnings, submit form, history with
      rejection reasons; shown only once approved.
- [x] Wizard Payment Details totals read the configured fees.
- [x] `/membership-fees` admin page + nav entry.

## 7. Tests

- [x] 15 new `PaymentServiceTests`, all passing first run:
      first due date from approval; **renewal advances from the previous due date, not today**
      (asserted with a deliberately late payment); idempotent verify; unapproved member refused;
      no-proof refused; reject records the reason and leaves membership untouched; reject-after-verify
      refused; second pending submission refused; `Kind` derived; future date refused; each payment
      keeps its own proof; fee fallback; fee round-trip; negative fee refused.
- [x] All 307 pre-existing tests still pass.

## 8. Docs

- [x] New `openspecs/payments.md`.
- [x] `openspecs/members.md` — the deferred Payments item marked built, the manual-`Status` note
      corrected.
- [x] This change package.

## 9. Verification

- [x] `dotnet build src/PSMPE.Portal.sln` — 0 warnings, 0 errors. **Stop the dev API first**; it
      locks the output DLLs (hit twice during this change).
- [x] `dotnet test src/PSMPE.Portal.sln --no-build` — 322 passing, 0 failing.
- [x] `npx tsc -b --noEmit`, `npm run lint` (0 errors, 3 known warnings), `npm run build`.

### Not yet done — needs a running app and a browser

- [ ] **New membership end to end**: register → submit → approve → member is still `Pending` → verify
      the payment → `Active`, due date one year after approval.
- [ ] **Renewal end to end**: set a due date in the past, confirm the grace warning and form appear,
      submit with proof, verify, confirm the new due date is **previous + 1 year** and the
      registration proof is still viewable.
- [ ] Reject a payment: the member sees the reason and can submit another.
- [ ] A second submission while one is pending is refused with a clear message.
- [ ] Change a fee on `/membership-fees`; confirm the wizard total, the renewal form's pre-filled
      amount, and a newly generated receipt all follow immediately (cache eviction working).
- [ ] Verify is disabled for a payment with no proof.
- [ ] Deleting a member with payment history is refused cleanly.
- [ ] Responsive at 375 / 768 / 1280 for the Payments tab, the renewal card and the fees page.
