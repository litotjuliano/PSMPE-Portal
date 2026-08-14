# Change: Replace Fake Dashboard with Real Membership Stats + Event/News Previews

## Status

**Implemented.** Built via subagent-driven development (implementer + spec-compliance review +
code-quality review per task, with fixes applied and re-verified after each review). Backend
build clean, **385 tests passing, 0 failures**. Frontend `tsc -b`/`eslint` clean throughout (0
errors; 3 pre-existing warnings in files this change never touched). Manual browser verification
of the Dashboard (both an Admin/staff view and a Member view, light and dark mode) has NOT been
performed in this environment — no browser-automation tooling was available, only `tsc`/`eslint`
and careful code reading plus independent re-verification of every reviewer's claims against the
actual diffs.

Reconstructed here, retroactively, from the actual implementation history — the design was
worked out via Claude Code's native Plan Mode (`docs/superpowers/specs/` was not used, and this
retroactive OpenSpec writeup was requested afterward for traceability). `tasks.md` in this
directory reflects the 8 commits actually made, marked complete.

## Why

The Dashboard page (`apps/web/src/integrations/template/pages/DashboardPage.tsx`) is a leftover
from the starter admin template. The content shown to Admin/staff users — revenue, orders,
traffic, top products — was 100% fake e-commerce filler with no backend behind it at all, while
Membership Registration (the one feature actually built end-to-end: `Member`/`Payment` entities,
full CRUD + approval workflow) had no dashboard presence whatsoever. Separately, Event Management
and News Management don't exist as real modules yet, but were identified as a key hook for
attracting prospective members — worth a visible placeholder on the dashboard now, even before
either module is built.

## What Changes

- **`GET /api/members/stats`** — new backend aggregation endpoint (status breakdown, 12-month
  registration trend, chapter/member-type breakdown, action items) backing a new Admin/staff-only
  "Statistics" section on the Dashboard, replacing the fake e-commerce widgets entirely.
- **Two static "Preview · Coming Soon" widgets** for Event Management and News Management —
  hardcoded mock content, clearly labeled as placeholder (badge + dashed border/tint + disclaimer
  text), no backend involvement. Unlike the Statistics section, these are shown to **every**
  role, Admin/staff and Member alike.
- **Deleted**: the entire old `apps/web/src/integrations/template/components/dashboard/` folder
  (11 files of fake e-commerce widgets and their chart-config data), which nothing else in the
  codebase referenced.

## Decisions

Each resolved by the user during brainstorming/plan review:

- **Visibility split**: the Statistics section is Admin/staff only (`!isMember` gate, matching
  who saw the old fake dashboard). The Events/News previews are visible to **all** roles,
  including Members — corrected mid-review from an initial draft that gated the whole thing
  Admin-only, once it was clarified that Events/News exist specifically as a member-recruitment
  hook, not an internal admin tool.
- **New backend aggregation endpoint, not client-side computation** — `GET /api/members/stats`
  computes counts/trends server-side rather than the frontend paginating/aggregating
  `GET /api/members` itself, so it scales correctly as real member data grows and keeps the
  frontend simple.
- **Dummy Event/News data must be unambiguously non-real** — a visible badge alone was judged
  insufficient; each preview widget also carries a second visual signal (dashed border + tint)
  and disclaimer text, so a badge overlooked doesn't leave anyone thinking these are live numbers.
- **Real Event Management, real News Management, and CPD Tracker are explicitly out of scope**
  for this change, and deliberately NOT three independent follow-ups either. Event Management and
  CPD Tracker are domain-coupled — members earn CPD points by attending events/seminars — so they
  must be designed together as one future initiative, not built piecemeal. News needs a
  fundamentally different auth/data model than the existing internal `ContentItem`/
  `ContentController` CMS (that controller is `[Authorize]`-gated with no public route, no
  publish/expiry dates, no media fields) and should become its own independent module later, not
  an extension of Content.
- **No caching on `GET /api/members/stats`** — this is one dashboard load, not a hot path;
  caching (with the invalidation wiring it would require across every `MemberService` write path)
  was judged unwarranted complexity for v1.
- **The `PendingPrcVerification` predicate must be shared, not duplicated**, between
  `GetAllAsync` (the existing Members list filter) and the new `GetStatsAsync` — surfaced during
  code-quality review as a drift risk (a future rule change to one copy could silently desync from
  the other with no compiler error), fixed by extracting a single
  `PendingPrcVerificationPredicate`.

## Design

### Backend

`src/PSMPE.Portal.Application/Members/Dtos/MemberStatsDto.cs` — `MemberStatsDto` (records:
`StatusCounts`, `RegistrationTrend`, `ByChapter`, `ByMemberType`, `ActionItems`). `MemberService
.GetStatsAsync` reuses `GetAllAsync`'s exact base filter (submitted, non-draft members, excluding
system/staff accounts), doing DB-side `GroupBy`/`CountAsync` aggregation (not an in-memory pull of
the whole table) for: status counts (zero-filled across all 4 `MembershipStatus` values); a
12-month registration trend (zero-filled gaps); chapter/member-type breakdowns projected onto the
full `Chapters.All`/`MemberTypes.All` constant lists in their declared order (zero-filled, so a
stray out-of-list value can't silently vanish from the chart); and three action-item counts
(pending approvals — `ApprovedAt == null`; pending PRC verification — the shared predicate above;
renewals due soon — Active members with `RenewalDueDate` within a local 60-day constant).
`MembersController.GetStats` exposes it at `GET /api/members/stats`, gated by the same
`Permissions.Members.View` as the existing list endpoint.

### Frontend

`apps/web/src/core/api/endpoints/memberApi.ts` gained a `MemberStats` interface (camelCase,
field-for-field matching the backend DTO) and a `getStats()` call. A new folder,
`apps/web/src/integrations/template/components/dashboard-membership/`, holds the Statistics
section: `MembershipDashboard.tsx` (fetches once, loading/error/cancellation-safe state, composes
the four widgets below plus `MembershipWelcomeBanner.tsx`, a trimmed/repurposed version of the old
template's welcome banner with its CTA repointed from `/content/new` to `/members`),
`MembershipStatusBreakdown.tsx` (4 `StatTile`s + a donut chart), `RegistrationTrendChart.tsx` (bar
chart), `MembershipBreakdownCharts.tsx` (chapter/member-type panels), and `ActionItemsWidget.tsx`
(3 `StatTile`s linking into `MembersPage`'s existing `?queue=approval`/`?queue=rmp` tabs). All
charts reuse the existing shared `ApexChart`/`StatTile` components rather than introducing a new
charting pattern.

A second new folder, `apps/web/src/integrations/template/components/dashboard-previews/`, holds
`EventsPreviewWidget.tsx` and `NewsPreviewWidget.tsx` — fully static, no API calls, no hooks,
hardcoded plausible mock content (a national convention, a CPD seminar, chapter meetups for
Events; a PRC exam schedule, a renewal deadline, a chapter election for News), each carrying a
"Preview · Coming Soon" badge (theme-aware, `bg-warning/10 text-warning dark:bg-warning/15`, not
a hardcoded `bg-white`) plus a dashed border/tint treatment and disclaimer text.

`DashboardPage.tsx` renders `{!isMember && <MembershipDashboard />}` followed, **outside** that
gate, by the two preview widgets — so Members see the previews but not the Statistics section,
while Admin/staff see both. The old fake dashboard's 11 files and its now-empty containing folder
were deleted; the only genuinely-reused pieces from it (`ApexChart.tsx`, `GaugeStat.tsx`,
`StatTile.tsx`, all under `components/shared/`) were kept in place.

## Not Changed (this round)

- No real Event Management module (entities, controllers, pages, nav entries) — the preview
  widget is 100% static mock data.
- No real News Management module — same.
- No CPD Tracker — explicitly deferred alongside Event Management as one future combined
  initiative, since CPD points are earned by event attendance.
- No changes to the existing member-facing banners (`CompleteApplicationBanner`, `ReceiptBanner`,
  `ProfileCompletenessGauge`) — untouched throughout.
- No caching layer on `GET /api/members/stats`.
- No deep-link query-param filtering was added to `MembersPage` — `ActionItemsWidget` only reuses
  the `?queue=approval`/`?queue=rmp` tabs that already existed there.
