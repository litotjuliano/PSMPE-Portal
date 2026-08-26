# Tasks: require-rmp-verification-before-approval

**Goal:** Make it impossible to admit a member to PSMPE before their RMP licence has been verified,
without turning that rule into a round trip between two tabs.

**Architecture:** A server-side refusal in `ApproveAsync` is the guarantee. A three-step
`ApproveApplicationWizard` is the UX that satisfies it in one place. A create-time licence
requirement closes the deadlock the gate would otherwise create for licence-less members.

**Tech Stack:** .NET 8 + EF Core (backend), React 19 + Vite + TypeScript (frontend). No test runner
exists in `apps/web`; the backend has xUnit unit and integration projects.

**Before starting:** read `proposal.md` in this folder.

---

## 1. The gate

- [x] `ApproveAsync` returns a failure when `PrcIdVerified` is false, with a message naming the
      licence as the blocker.
- [x] Placed **after** the already-approved short-circuit, so pre-existing approvals stand and
      repeat calls on them still succeed. Comment says why the placement matters.

## 2. Close the licence-less deadlock

- [x] `CreateAsync` rejects a blank `PrcLicenseNo`, with a comment tying it to the verification
      filter and the gate.
- [x] `MemberFormCard`'s RMP License No. input gains `required`.

## 3. The review wizard

- [x] New `components/shared/ApproveApplicationWizard.tsx`: step 1 RMP Licence (Verify / Reject,
      with the uploaded ID viewable), step 2 Membership ID, step 3 Confirm.
- [x] Already-verified members open at step 2; Back is hidden there since there is no step 1 to
      return to.
- [x] Reject ends the flow — the application stays unapproved, the member stays in the RMP queue
      with the reason recorded.
- [x] Membership ID field carries over the debounced availability check, the out-of-order response
      guard, the length cap, and the aria-live status line.
- [x] A failed approval returns to step 2 rather than closing, so a duplicate ID can be corrected
      without losing the flow.
- [x] Composes `PipeStepper`, `FilePreviewModal`, `ConfirmationModal` (`reasonRequired`) rather than
      reimplementing any of them.

## 4. Wire it in

- [x] `MembersPage` passes the pending member straight to the wizard; its own approve handler is
      gone (the wizard owns the call).
- [x] `MemberFormPage` keeps the loaded `Member` alongside its form state — the wizard needs the
      licence, pending licence and verification flag, none of which `MemberFormState` holds.
- [x] Delete `ApproveMembershipModal.tsx`; swap the export in `integrations/template/index.ts`.

## 5. Tests

- [x] `VerifyRmpAsync` helper; applied to all 13 approvals the gate broke.
- [x] `GetAll_WithPendingPrcVerificationOnly_...` no longer builds its licence-less member through
      the API (which now refuses it) — seeded into the context instead, with a comment explaining
      that the shape survives only as legacy data.
- [x] New: approving unverified is rejected, leaves `ApprovedAt` null **and** does not assign the
      number.
- [x] New: verify → approve succeeds and assigns the number.
- [x] New: create without a licence fails without persisting (null, empty, whitespace).
- [x] Idempotency test keeps its single verification — re-verifying an already-approved member would
      be noise.

## 6. Docs

- [x] `openspecs/members.md` — new "RMP verification gates approval" section; `POST /approve` and
      `POST /api/members` request docs updated; the `ApproveMembershipModal` reference replaced.
- [x] This change package.

## 7. Verification

- [x] `dotnet build src/PSMPE.Portal.sln` — 0 warnings, 0 errors. **Stop the dev API first**; it
      locks the output DLLs and the build fails with MSB3027. (Hit this mid-run: the integration
      tests silently ran against a stale build and under-reported failures until it was stopped.)
- [x] `dotnet test src/PSMPE.Portal.sln --no-build` — 307 passing, 0 failing.
- [x] `npx tsc -b --noEmit` and `npm run lint` in `apps/web` — 0 errors, only the 3 known
      pre-existing warnings.
- [x] `npm --prefix apps/web run build`.

### Not yet done — needs a running app and a browser

- [ ] **The reported scenario**: Test 123 in Pending Approval → Approve opens the wizard at step 1,
      not the ID dialog, and cannot reach step 3 without verifying.
- [ ] Reject at step 1 leaves the member unapproved and in the RMP queue with the reason recorded.
- [ ] An already-verified member opens at step 2 with no Back button.
- [ ] The 4 pre-existing approved members are untouched — re-approving still succeeds and does not
      renumber them.
- [ ] A duplicate Membership ID still blocks at step 2 with the wizard staying open.
- [ ] Admin create without an RMP licence is refused client-side and server-side.
- [ ] The standalone RMP Verification tab still works for an approved member changing their licence.
- [ ] Responsive at 375 / 768 / 1280 — the stepper must not overflow the modal on mobile.
