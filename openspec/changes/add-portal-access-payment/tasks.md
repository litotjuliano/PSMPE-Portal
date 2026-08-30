# Tasks: add-portal-access-payment

**Goal:** Make portal access a recurring, per-payment add-on — an always-optional checkbox at
registration and every renewal, priced and enforced without any global admin mode to switch — plus
the supporting promotional-pricing, mistake-guarding, and reporting pieces that came out of design.

**Architecture:** Two new `Payment`/`Member` columns driven entirely by `PaymentVerification.Apply`;
a small `FeePromotion` table resolved on every fee read; a second independent condition on
`MembershipAccessMiddleware`; admin-side UI to configure fees/promotions and review payments; a
summary reporting endpoint.

**Tech Stack:** .NET 8 + EF Core + Postgres, React 19 + Vite + TypeScript. No test runner in
`apps/web`; the backend has xUnit unit and integration projects.

**Before starting:** read `proposal.md` in this folder, then re-verify the "Branching & rollout"
snapshot below is still accurate (other branches may have landed since this was written).

---

## 0. Pre-flight (do this first)

- [x] `git fetch`, `git branch -a`, `git worktree list` — confirm no new in-progress work overlaps
      the critical files listed in `proposal.md`'s "What Changes" (last checked 2026-08-30: only
      `feature/smtp-email-sender` was in progress, touching two unrelated new files).
- [x] Branch off the latest `origin/develop` into a dedicated feature branch (e.g.
      `feature/portal-access-payment`).
- [ ] Grep for `RegistrationTotal`/`registrationTotal` across `src/` and `apps/web/src/` to enumerate
      every consumer before the breaking rename in step 4.
- [ ] Locate and read `ReceiptGenerator.cs` (not opened during design) to confirm how it consumes fee
      totals before changing `MembershipFeesDto`.

## 1. Domain and persistence

- [x] `Payment` gains `bool IncludesPortalAccess` (default `false`) and `decimal PortalFeeAmount`
      (default `0`).
- [x] `Member` gains `bool HasPortalAccess` (default `false`), documented as written exclusively by
      `PaymentVerification.Apply`.
- [x] New `FeePromotion` entity: `Id`, `FeeKey` (string), `PromoAmount` (decimal), `StartDate`/
      `EndDate` (`DateOnly`), `CreatedByUserId`, `CreatedAt`. Own EF configuration.
- [x] One migration adding both `Payment`/`Member` columns and the `FeePromotions` table.
      (Commits `6f9a828`, `803400c` — the latter fixing a missing decimal precision on
      `PortalFeeAmount` found in code review.)

## 2. Fee configuration and promotional pricing

- [x] `PortalFee` added to `MembershipFeeKeys` (default `900m`), picked up automatically by
      `SystemConfigSeeder`'s per-key seeding.
- [x] Fee-resolution function (`FeePromotionResolver.ResolveAsync`) that overrides a `SystemConfig`
      amount with an active `FeePromotion`'s `PromoAmount` when `StartDate <= asOf <= EndDate`. Pure
      lookup, no caching of its own, no background job.
- [x] Reject overlapping `FeePromotion` date ranges for the same `FeeKey` on create.
- [x] `POST /api/payments/fees/promotions`, `GET /api/payments/fees/promotions`,
      `DELETE /api/payments/fees/promotions/{id}` (`members:manage`).
- [x] Routed `PaymentService.GetFeesAsync` and `MemberService.EnsureRegistrationPaymentAsync` through
      the resolver. ("The admin walk-in default" from the original wording doesn't exist as a real
      call site — `ResolveRegistrationPaymentAsync` has no default-amount computation at all; the
      admin types the amount directly. Nothing to route there.)
- [x] Unit test: fee edit via `UpdateFeesAsync`, then verify an already-submitted payment — confirms
      `Amount`/`IncludesPortalAccess` untouched.
      (Commits `967fb39`, `8c68df3` — the latter reusing `MembersController`'s existing
      `ToActionResult(Result<T>)` pattern instead of an incomplete inline switch, per code review.)

## 3. Application layer

- [x] `PaymentVerification.Apply` sets `member.HasPortalAccess = payment.IncludesPortalAccess`
      alongside the existing `Status`/`RenewalDueDate` writes.
- [x] `SubmitPaymentRequest` gains `bool IncludePortalAccess = false`;
      `PaymentService.SubmitAsync` sets `Payment.IncludesPortalAccess` directly from it (renewal
      path).
- [x] `POST /api/members/me/submit` gains optional `includePortalAccess`, threaded through
      `SubmitMyProfileAsync(userId, includePortalAccess, ct)` into `EnsureRegistrationPaymentAsync`,
      which adds `PortalFee` to the computed default amount when ticked (registration path).
- [x] `RecordPaymentRequest` / `ApproveMemberRequest.Payment` gains `bool IncludePortalAccess = false`
      for the admin walk-in path (also the paper-form-intake path).
- [x] `MembershipFeesDto`: replace `RegistrationTotal` with `PortalFee` and four explicit totals
      (`RegistrationTotalWithoutPortal`, `RegistrationTotalWithPortal`, `RenewalTotalWithoutPortal`,
      `RenewalTotalWithPortal`); update every consumer found in step 0.
      (`UpdateMembershipFeesRequest` also gained `PortalFee`, since task 2 added the config key but
      no write path for it - `UpdateFeesAsync` now persists it.)
- [x] `MemberDto` gains `bool HasPortalAccess`, read directly off `Member.HasPortalAccess`.
      (Note: `Payment.PortalFeeAmount`, added in task 1, is now stamped on all three
      payment-creation paths this task touches - `PaymentService.SubmitAsync`,
      `MemberService.EnsureRegistrationPaymentAsync`, and `ResolveRegistrationPaymentAsync` - each
      resolving the currently-effective `PortalFee` through `FeePromotionResolver` independently of
      the payment's own `Amount`, so a later fee/promo edit can never retroactively change what a
      historical payment's portal-revenue contribution was. Originally left unset in the first pass
      of this task and caught in spec review before task 7 could ship a reporting endpoint that
      silently summed zero; fixed in the same task.)

## 4. Access enforcement

- [x] `MembershipAccessMiddleware`: second independent check — 403 `PORTAL_ACCESS_REQUIRED` when
      `member.HasPortalAccess == false` and `member.Status != MembershipStatus.Deactivated`, checked
      *after* the existing `Status == Expired` check (so a member failing both sees
      `MEMBERSHIP_EXPIRED`). Reuses `[AllowExpiredMember]` unchanged as the allowlist.
- [x] `ExpiredMembershipGate.tsx`: `useMembershipAccess()` returns
      `{ isExpired, lacksPortalAccess, isRestricted }`; redirect-to-`/profile` condition extended to
      `isRestricted`.
- [x] `AppMenu.tsx`: `keepProfileOnly(...)` keyed on `isRestricted` instead of `isExpired`.
      (`apps/web/src/core/types/member.ts` also gained `hasPortalAccess: boolean`, pulled forward
      from task 6 since this task's frontend gate can't compute `lacksPortalAccess` without it -
      task 6's own copy of this item is a no-op when that task runs.)

## 5. Mismatch guarding (frontend only, no backend validation)

**Scope correction found before starting this task**: the admin walk-in payment form referenced
below is `apps/web/src/integrations/template/components/shared/ApproveApplicationWizard.tsx` — it
still reads `fees.registrationTotal` (twice), a field task 3 removed from the backend response.
Neither this task nor task 6 originally listed the shared frontend type files that need updating
for that to even compile against real data. Both are now in scope here, since this task is the
first one that needs a working, portal-aware `MembershipFees` type on the frontend. **Second gap
found**: `PaymentDto` (`src/PSMPE.Portal.Application/Payments/Dtos/PaymentDto.cs`) never exposes
`IncludesPortalAccess` at all — it's only on the `Payment` domain entity — so the admin queue table
below has no way to know it. Backend fix needed first:

- [x] `PaymentDto` gains `bool IncludesPortalAccess`; `PaymentService.ToDto` passes it through.
- [x] `paymentApi.ts`/`memberApi.ts` types reshaped (`MembershipFees` gains `portalFee` + four
      totals, `Payment.includesPortalAccess`, `SubmitPaymentRequest.includePortalAccess?` made
      *optional* — deliberately, so `RenewalPaymentCard.tsx`'s not-yet-updated call site keeps
      compiling until task 6 — `approveMember`'s `includePortalAccess` required, `updateFees`'s
      `portalFee`).
- [x] `ApproveApplicationWizard.tsx`: both stale totals fixed, checkbox added with auto-sync from
      `Amount`, a `portalManuallyToggledRef` latches once the admin directly toggles it so a later
      unrelated amount edit can't silently discard their override (added after code review caught
      the original naive recompute-on-every-keystroke version doing exactly that), non-blocking
      caution in both the walk-in and read-only review branches, Confirm-step note,
      `includePortalAccess` threaded to `approveMember`.
- [x] `PaymentsQueueTable.tsx`: mount-only fees fetch, per-row caution badge comparing `Amount`
      against the expected total for `IncludesPortalAccess` given `Kind` (registration totals for
      `NewMembership`, renewal totals otherwise).
      (Two extra stale-`registrationTotal` consumers found and minimally fixed along the way,
      compile-preserving only, real UI deferred to task 6: `MembershipFeesPage.tsx`'s `portalFee`
      round-tripped but not yet editable; `MembershipApplicationWizardCard.tsx`'s Step 3 total swapped
      to `registrationTotalWithoutPortal`, no checkbox added yet. Commits `0dd7de7`, `6340810`,
      `f413709`, `1ca6b7b`, `5438d54`.)

## 6. Frontend UI

`apps/web/src/core/types/member.ts`'s `hasPortalAccess: boolean` was already added in task 4 (pulled
forward — see that section's note). **Reordering note**: the original "Admin Payments tab: summary
panel" bullet here is moved to task 7, since it displays data from that task's reporting endpoint,
which doesn't exist yet — building it here would have nothing to call.

- [x] `paymentApi.ts` gained the three promotion client methods.
- [x] `MembershipFeesPage.tsx`: Portal Fee is now a real fourth editable field; Promotions panel
      added with a Status filter and (added after code review, matching `AdminUsersPage.tsx`'s
      search+categorical-filter precedent) a Fee filter, add-form with Single-day checkbox, delete
      via `ConfirmationModal`. Commits `4940c9b`, `a2fa434`.
- [x] `MembershipApplicationWizardCard.tsx` (Step 3) + `MyProfilePage.tsx`: "Include Portal Access
      (+₱{portalFee})" checkbox on `MembershipApplicationState`, recomputed TOTAL (currently hardcoded
      to `registrationTotalWithoutPortal` per task 5's minimal compile-fix), threaded through
      `handleWizardSubmit` → `memberApi.submitMyProfile()` (currently takes no arguments —
      `POST /api/members/me/submit` already accepts an optional `includePortalAccess` body per task 3,
      just never sent from here) as `includePortalAccess`.
- [x] `RenewalPaymentCard.tsx`: current portal status shown (`member.hasPortalAccess`); same opt-in
      checkbox, pre-checked to it; pre-filled amount follows the checkbox
      (`renewalTotalWithoutPortal`/`WithPortal` instead of the current bare `annualDues`); threaded
      through `paymentApi.submitMyPayment(...)` as `includePortalAccess` (already accepted, optional,
      per task 5). An initial `includePortalAccessRef` workaround for `load()`'s stale closure was
      simplified away in code review — `load` now takes the checkbox value as a parameter instead.
      Commits `01e2665`, `209a115`.

## 7. Payment reporting

- [x] `GET /api/payments/reports/summary?startDate=&endDate=` (`Permissions.Members.View`): filters
      to `Verified` `NewMembership`/`Renewal` payments, inclusive date range on `PaidOn`
      (`EventRegistration` and non-`Verified` payments excluded); `PaymentReportSummaryDto` with
      membership-only count/total, combined count/total, portal revenue total (summed explicitly
      over the combined subset, not relying on the zero-invariant). Inverted range rejected as a
      `Result.Failure` in the service layer so it's unit-testable. Code review collapsed a third
      near-duplicate `ToActionResult<T>` overload into one generic version and added explicit
      boundary-date tests (`PaidOn == startDate`/`== endDate`). Commits `886f4ae`, `f301ca5`.
- [x] Admin Payments tab: new `PaymentsSummaryPanel.tsx` (self-contained, sibling to
      `PaymentsQueueTable.tsx`), rendered above it in `MembersPage.tsx`. Month quick-pick (This
      month/Last month/Last 3/Last 6 months/This year) plus a cross-constrained custom date range,
      three stat tiles. Code review fixed the quick-pick `<select>` being uncontrolled (re-selecting
      an already-displayed preset after a manual date edit was a silent no-op — now tracks the
      current pick as state, including a "Custom range" label once diverged). Commits `bb1e67a`,
      `8e58fc1`.

## 8. Documentation

- [x] `openspecs/payments.md`: four new sections added, verification table and access-restriction
      paragraph updated, two new "Not built" items added, Fees/Endpoints sections updated. Two review
      rounds: (1) corrected the access-restriction wording — the `PORTAL_ACCESS_REQUIRED` condition
      has no Active-only guard and also catches `Pending` applicants by construction, which the first
      draft mischaracterized; (2) named `MembershipFeesDto`'s four total properties, added
      `RenewalPaymentCard`'s checkbox to "Member UI," fixed a GET/POST/DELETE ordering inconsistency.
      Commits `fe4fe19`, `746f0b0`, `0ae9d2a`.

## 9. Tests

Every item below was already written and reviewed as part of the task that introduced the behavior,
not as a separate pass — re-confirmed here against a fresh `dotnet test` run rather than trusted from
the task list (checkboxes lag reality easily on a long-running branch like this one).

- [x] Verifying a payment sets `Member.HasPortalAccess` to match `Payment.IncludesPortalAccess`
      (both true/false); a renewal omitting portal revokes prior access. (Task 3.)
- [x] `MembershipAccessMiddleware` 403s with `PORTAL_ACCESS_REQUIRED` for a member with
      `HasPortalAccess=false` outside the allowlist (any non-`Deactivated` `Status`, including
      `Pending` — see task 8's correction); passes for allowlisted routes; does not 403 a Deactivated
      member. (Task 4.)
- [x] Fee edit doesn't retroactively change a pending/verified payment. (Task 2.)
- [x] `FeePromotion` active today resolves to `PromoAmount`; one outside its range does not;
      overlapping promo for the same `FeeKey` is rejected; a payment created during a promo window
      keeps its captured amount after the promo expires. (Task 2.)
- [x] Reports summary endpoint buckets membership-only vs. combined correctly, sums
      `PortalFeeAmount` only for the combined subset, excludes `EventRegistration` and
      non-`Verified` payments, and covers the inclusive date boundary exactly. (Task 7a.)
- [x] All pre-existing tests still pass — fresh run: **530/530** (212 Application.UnitTests + 19
      Infrastructure.UnitTests + 299 WebAPI.IntegrationTests), 0 build warnings/errors.

## 10. Verification

- [ ] `dotnet build` clean, `dotnet test` all passing.
- [ ] `npx tsc -b --noEmit`, `npm run lint`, `npm run build` clean.
- [ ] Manual: register ticking/unticking the Portal Fee checkbox, confirm total and submitted flag
      match; verify both kinds of payment as admin, confirm a member without portal access collapses
      to Profile-only nav until a renewal that includes it.
- [ ] Manual: fee edit on `/membership-fees` doesn't change a payment already in the admin queue.
- [ ] Manual: add a promotion covering today, confirm wizard/renewal totals reflect it, confirm it
      stops applying once the end date passes.
- [ ] Manual: admin walk-in form — typed amount auto-syncs the checkbox; manual override against the
      amount shows the caution.
- [ ] Manual: payments summary panel figures match the underlying verified payments for a chosen
      date range.

## 11. Rollout

- [ ] PR into `develop` (rebase on `origin/develop` again first, resolving any conflicts in the
      feature branch).
- [ ] Merge/push `develop` into `staging`, re-run the verification list above against the staging
      environment (real DB, real migration run, real fee-edit/promo timing).
- [ ] Only after staging passes, merge into `main`.
