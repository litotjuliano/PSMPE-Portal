# Real Membership Dashboard + Event/News Previews — Implementation Tasks

> Reconstructed retroactively from the actual implementation history (this feature was built via
> Claude Code's native Plan Mode + `superpowers:subagent-driven-development`, not from a
> pre-written OpenSpec `tasks.md`). All boxes below are checked because the work is complete and
> merged to `develop`; each references the real commit(s) that did it.

**Goal:** Replace the Dashboard's fake e-commerce admin content with a real Membership statistics
section, and add dummy "coming soon" preview widgets for Event Management and News Management —
per `proposal.md` and its two spec deltas (`membership-dashboard-stats`, `dashboard-previews`).

**Verification throughout:** backend — `dotnet test src/PSMPE.Portal.sln` (baseline 367 passing,
0 failures before this change; 385 passing, 0 failures after). Frontend — `npx tsc -b` and
`npx eslint .` from `apps/web` (no frontend test runner exists in this project).

---

## 1. Backend: `GET /api/members/stats`

- [x] **`MemberStatsDto` and sub-records** — `src/PSMPE.Portal.Application/Members/Dtos/MemberStatsDto.cs`
- [x] **`MemberService.GetStatsAsync`** — DB-side aggregation (status counts, 12-month trend,
      chapter/member-type breakdowns, action items), reusing `GetAllAsync`'s base filter
- [x] **`MembersController.GetStats`** — `GET /api/members/stats`, `members:view` permission
- [x] **Unit tests** — `tests/PSMPE.Portal.Application.UnitTests/Members/MemberServiceStatsTests.cs`
      (zero-fill correctness, excluded system accounts, excluded drafts, renewal-due-soon
      boundary on both sides)
- [x] **Integration tests** — auth coverage (200/401/403) added to
      `tests/PSMPE.Portal.WebAPI.IntegrationTests/Members/MembersControllerAuthTests.cs`
- [x] **Code review fix**: extracted shared `PendingPrcVerificationPredicate` (was duplicated
      between `GetAllAsync` and `GetStatsAsync`); added the missing exact-boundary test cases
      (59/60/61/0/-1 days) that the first pass had omitted

Commits: `4f5e1fd` (feat: add Membership stats aggregation endpoint), `c1a9e29` (fix: address
code review findings on Membership stats endpoint)

## 2. Frontend: Membership statistics widgets

- [x] **API client** — `MemberStats` interface + `getStats()` in
      `apps/web/src/core/api/endpoints/memberApi.ts`
- [x] **`dashboard-membership/` folder** — `MembershipStatusBreakdown.tsx`,
      `RegistrationTrendChart.tsx`, `MembershipBreakdownCharts.tsx`, `ActionItemsWidget.tsx`,
      `MembershipDashboard.tsx` (container: fetch + compose)
- [x] **Welcome banner** — trimmed/repurposed version of the old template's `WelcomeUser`, CTA
      repointed to `/members`
- [x] **Code review fix**: extracted `MembershipWelcomeBanner` into its own file (was inlined
      into `MembershipDashboard.tsx`, bloating it to 235 lines and mixing three concerns)

Commits: `d8cc204` (feat: add Membership statistics dashboard widgets), `87e2668` (refactor:
extract MembershipWelcomeBanner into its own file)

## 3. Frontend: Event/News preview widgets

- [x] **`dashboard-previews/` folder** — `EventsPreviewWidget.tsx`, `NewsPreviewWidget.tsx`,
      fully static, plausible mock content, "Preview · Coming Soon" badge + dashed border/tint +
      disclaimer text
- [x] **Code review fix**: badge color switched from hardcoded `bg-white` (broken in dark mode)
      to the theme-aware `bg-warning/10 text-warning dark:bg-warning/15` pattern already used by
      `StatusBadge.tsx`

Commits: `16eb023` (feat: add dummy coming-soon preview widgets for Event and News Management),
`5d8e5cd` (fix: use theme-aware warning tint for preview badge, not hardcoded white)

## 4. Wire into `DashboardPage.tsx` and delete the old fake dashboard

- [x] **`DashboardPage.tsx`** — `MembershipDashboard` gated `{!isMember && ...}`; Events/News
      preview widgets rendered unconditionally (all roles)
- [x] **Deleted** the 11 old fake-dashboard files (`ProductOrderDetails.tsx`,
      `SalesRevenueOverview.tsx`, `CustomerService.tsx`, `Audience.tsx`, `SalesThisMonth.tsx`,
      `TopSellingProducts.tsx`, `OrderStatistics.tsx`, `TrafficResources.tsx`,
      `ProductOrders.tsx`, `WelcomeUser.tsx`, `data.ts`) and the now-empty `dashboard/` folder;
      re-verified via fresh grep that nothing else referenced them
- [x] **Code review fix**: cleaned up 3 stale forward-referencing comments/docs (two doc comments
      that still said "not yet wired in", plus `template/README.md`'s folder map still listing
      the deleted `dashboard/` folder)

Commits: `92cb0f2` (feat: wire real Membership dashboard and Event/News previews into
DashboardPage), `11a2d6a` (docs: clean up stale forward references now that dashboard wiring is
done)

## 5. Final verification

- [x] Full backend suite: `dotnet test src/PSMPE.Portal.sln` → 385 passed, 0 failed
- [x] Frontend: `npx tsc -b` clean; `npx eslint .` → 0 errors (3 pre-existing, unrelated warnings)
- [x] Independent final holistic review across all 8 commits — confirmed DTO-to-UI field parity,
      correct role-gating (traced through the real JSX, not just claimed), no leftover
      `TODO`/`console.log`, permission boundary traced to role-seeding level, no dangling
      references to deleted files
- [x] Pushed to `origin/develop`
- [x] `openspecs/members.md` updated to document `GET /api/members/stats` (commit `ab5d0ad`)

## Not done in this change (see proposal.md "Not Changed")

- [ ] Real Event Management module — deferred, to be designed together with CPD Tracker
- [ ] Real News Management module — deferred, as its own independent module
- [ ] CPD Tracker — deferred, domain-coupled to Event Management
- [ ] Manual browser verification (no browser-automation tooling available in this environment)
