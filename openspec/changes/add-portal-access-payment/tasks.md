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

- [ ] Admin walk-in payment form: as `Amount` is typed, auto-set the Portal Access checkbox
      (checked once `Amount >= withPortalTotal`); allow manual override.
- [ ] Inline, non-blocking caution when the checkbox state doesn't match what the typed amount
      implies (either auto-synced-then-overridden, or typed after toggling).
- [ ] Admin Payments queue (`Submitted` list): caution badge/icon on any pending payment whose
      `Amount` doesn't match its own `IncludesPortalAccess`, so a self-submitted member mismatch is
      visible before Verify.

## 6. Frontend UI

- [ ] `MembershipFeesPage.tsx`: fourth editable field (Portal Fee) + Promotions panel (table with
      active/upcoming/expired status; add form with fee/amount/date-range and a "Single day"
      checkbox that sets `StartDate = EndDate`; cancel action).
- [ ] `MembershipApplicationWizardCard.tsx` (Step 3): "Include Portal Access (+₱{portalFee})"
      checkbox, recomputed TOTAL, sent as `includePortalAccess` on submit.
- [ ] `RenewalPaymentCard.tsx`: current portal status shown; same opt-in checkbox, pre-checked to
      `hasPortalAccess`; pre-filled amount follows the checkbox.
- [ ] `apps/web/src/core/types/member.ts`: add `hasPortalAccess: boolean`.
- [ ] Admin Payments tab: summary panel (date-range picker — month quick-pick + custom range —
      showing membership-only count/total, combined count/total, portal revenue collected).

## 7. Payment reporting

- [ ] `GET /api/payments/reports/summary?startDate=&endDate=` (`members:view`): for `Verified`
      `NewMembership`/`Renewal` payments with `PaidOn` in range (excludes `EventRegistration`) —
      membership-only count/`SUM(Amount)`, combined count/`SUM(Amount)`, and
      `SUM(PortalFeeAmount)` for combined payments only.

## 8. Documentation

- [ ] `openspecs/payments.md`: new sections for "Portal access is a per-payment add-on," "Fee edits
      are prospective only," "Promotional pricing," and "Payment reporting"; update the verification
      effects table and the access-restriction paragraph (second `PORTAL_ACCESS_REQUIRED` condition,
      ordering rule); add the two new "Not built" items (mid-cycle upgrade, bulk import).

## 9. Tests

- [ ] Verifying a payment sets `Member.HasPortalAccess` to match `Payment.IncludesPortalAccess`
      (both true/false); a renewal omitting portal revokes prior access.
- [ ] `MembershipAccessMiddleware` 403s with `PORTAL_ACCESS_REQUIRED` for an Active member with
      `HasPortalAccess=false` outside the allowlist; passes for allowlisted routes; does not 403 a
      Deactivated member on this check.
- [ ] Fee edit doesn't retroactively change a pending/verified payment (see step 2).
- [ ] `FeePromotion` active today resolves to `PromoAmount`; one outside its range does not;
      overlapping promo for the same `FeeKey` is rejected; a payment created during a promo window
      keeps its captured amount after the promo expires.
- [ ] Reports summary endpoint buckets membership-only vs. combined correctly and sums
      `PortalFeeAmount` only for combined payments in range, excluding `EventRegistration` and
      non-`Verified` payments.
- [ ] All pre-existing tests still pass.

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
