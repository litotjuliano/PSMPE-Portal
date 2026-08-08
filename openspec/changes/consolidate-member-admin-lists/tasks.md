# Tasks: consolidate-member-admin-lists

**Goal:** Collapse Members, Membership Approvals and RMP Verifications — three nav entries, three
pages, three tables, one underlying query — into a single `/members` page with three tabs, without
merging the two genuinely different approval decisions.

**Architecture:** Frontend consolidation. `GET /api/members` already supported both queue filters
and all three mutation endpoints already existed, so the backend change is a single sort arm.
`MembersTable` gains a `view` prop and becomes the only member table; `MembersPage` owns the tabs,
the counts and both modals.

**Tech Stack:** React 19 + Vite + TypeScript (frontend), .NET 8 + EF Core (backend, one line).
No test runner exists in `apps/web`; the backend has xUnit unit and integration projects.

**Before starting:** read `proposal.md` in this folder.

---

## 1. Backend

- [x] Add a `"submittedat"` arm to `MemberService.GetAllAsync`'s sort switch, with a comment on why
      a work queue wants oldest-first.
- [x] Add `'submittedAt'` to `GetMembersParams['sortBy']` in `memberApi.ts` to match.

## 2. One table, three views

- [x] `MembersTable` takes `view: 'all' | 'pendingApproval' | 'pendingRmp'` (exported as
      `MembersView`) plus optional per-view handlers, each documented as to which view needs it.
- [x] Shared columns Name / Membership No. / Chapter, sortable in every view; per-view tail columns
      and a per-view action column.
- [x] Carry across everything the deleted tables did: the delete `ConfirmationModal`, the RMP reject
      `ConfirmationModal` with `reasonRequired`, and the `FilePreviewModal` for the uploaded RMP ID.
- [x] "Applied" reads `submittedAt`, not `createdAt` — the old table showed when the draft row was
      created, not when the member applied.
- [x] Per-view `colSpan` on the empty row, and per-view empty-state copy.

## 3. One page, three tabs

- [x] `MembersPage` holds the tab strip, reading and writing `?queue=` via `useSearchParams` so the
      active tab is linkable.
- [x] Per-tab default sort — oldest-first for the approval queue, alphabetical for the rest — reset
      on tab change along with the page number.
- [x] Tab count badges from two `pageSize: 1` calls; refetch after every approve/reject/delete.
- [x] A failed count is swallowed, never surfacing as a page-level error over a list that loaded.
- [x] Both modals lifted here, including the deliberate rethrow that keeps the approve dialog open
      on a duplicate Membership ID.
- [x] Tab strip scrolls rather than wraps on narrow screens.

## 4. Remove the old surfaces

- [x] Delete `MembershipApprovalsPage.tsx`, `PrcVerificationsPage.tsx`,
      `MembershipApprovalsTable.tsx`, `PrcVerificationsTable.tsx`.
- [x] Drop the two table exports from `integrations/template/index.ts`; export `MembersView`.
- [x] `/membership-approvals` and `/prc-verifications` become `<Navigate replace>` redirects.
- [x] Drop the two `SideNav` entries and the icons that became unused.
- [x] Repoint the topbar bell and `NotificationsList` RMP items at `/members?queue=rmp`. Leave the
      membership-application items pointing at `/members/:id` — the individual application is more
      useful than a queue (see proposal, "Deviation From The Plan").

## 5. Tests

- [x] `GetAllAsync_SortedBySubmittedAt_OrdersOldestApplicationFirst` — asserts both directions.
- [x] Existing 301 tests still pass unchanged.

## 6. Docs

- [x] `openspecs/members.md` — new "One Members page, three tabs" section with the tab table and the
      two-decisions distinction; `GET /api/members` query docs updated for `submittedAt` and
      `pendingPrcVerificationOnly`; every stale reference to the removed pages and routes corrected.
- [x] This change package.

## 7. Verification

- [x] `dotnet build src/PSMPE.Portal.sln` — 0 warnings, 0 errors. **Stop the dev API first**; it
      locks the output DLLs and the build fails with MSB3027 otherwise.
- [x] `dotnet test src/PSMPE.Portal.sln --no-build` — 302 passing, 0 failing.
- [x] `npx tsc -b --noEmit` and `npm run lint` in `apps/web` — 0 errors, only the 3 known
      pre-existing warnings (`ApexChart`, `useLayoutContext` ×2).
- [x] `npm --prefix apps/web run build`.

### Not yet done — needs a running app and a browser

- [ ] Both workflows end to end: approve an application (Membership ID dialog, duplicate still
      blocked and dialog stays open), then verify and separately reject an RMP change (reason
      required, member notified, audit row written).
- [ ] A member needing **both** decisions appears in both queue tabs, and approving one leaves the
      other pending.
- [ ] Tab counts match the row counts and drop immediately after a decision.
- [ ] `/membership-approvals` and `/prc-verifications` land on the right tab; the notification bell
      and the Notifications list do too.
- [ ] Nothing lost from the old tables: the RMP tab can still preview the uploaded RMP ID, and the
      All tab still edits and deletes.
- [ ] Sorting works on every tab, and "Applied" sorts oldest-first by default.
- [ ] Responsive at 375 / 768 / 1280 — the tab strip must scroll, not push the card wide.
