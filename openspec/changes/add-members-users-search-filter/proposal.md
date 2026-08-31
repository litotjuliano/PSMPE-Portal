# Change: Add Search and Filter to the Members and Users Lists

## Status

**Implemented.** Built via subagent-driven development, with a two-stage (spec-compliance and
code-quality) review after each task. Backend build clean, **329 tests passing** (up from 325),
frontend typecheck/lint/build clean. See `tasks.md` for what remains unverified (the live-browser
pass).

## Why

`GET /api/members` and `GET /api/admin/users` are both paginated, sortable lists with no free-text
search and, for Users, no filter of any kind. `MemberService.GetAllAsync` already accepts a
`status` filter, but the Members UI never exposed it as a control. As the row count grows (real
members, plus more admin/staff roles like the new Approval role mixed into the Users list),
finding one specific record means paging through everything.

The user separately asked that this become a standing rule: every list, current and future
module alike, should ship with search and filter, not sorting alone.

## What Changes

- **Members "All Members" tab**: a debounced free-text search box (matches name, Membership No.,
  email) plus a Status filter dropdown (the filter already exists server-side; it just had no UI).
- **Users list**: a debounced free-text search box (matches display name, email) plus a Role
  filter (multi-select).
- Both server-side, via new `search` (and `roles`, for Users) query parameters — required because
  these lists are paginated; a client-side-only filter would silently miss rows outside whatever
  page happens to be loaded.
- Both reset to page 1 when the search term or filter changes, matching how tab/sort changes
  already reset pagination on these pages.

## Decisions

Each resolved by the user during brainstorming:

- **Scope this round**: Members "All Members" tab and the Users list only. The three queue tabs
  (Pending Approval, RMP Verification, Payments) are short-lived work queues, typically a handful
  of rows, and are deliberately left as-is for now.
- **Members filter**: Status only. A Chapter filter was considered and deferred.
- **Users filter**: Role, in addition to search.
- **Server-side search**, not client-side filtering of the loaded page — the correct behavior
  under pagination.

## Design

- **Backend**: `MemberService.GetAllAsync` gains a `string? search` parameter, matching
  (case-insensitive, via `.ToLower().Contains(...)` — the same idiom already used for
  `MembershipNo` comparisons elsewhere in that file) against `FirstName`, `LastName`,
  `MembershipNo`, and the linked `User.Email`. `AdminController.GetUsers` gains `string? search`
  (matches `DisplayName`/`Email`, same style) and `IReadOnlyCollection<string>? roles`, the latter
  reusing the exact pattern already in that method for hiding Super Admin rows
  (`userManager.GetUsersInRoleAsync(role)` → a `HashSet<Guid>` of matching ids →
  `query.Where(u => idSet.Contains(u.Id))`, unioned across the selected roles).
- **Frontend**: a debounced (~350ms, matching the debounce style already used in
  `ApproveApplicationWizard.tsx`) search input on each page, wired into the existing
  `fetchList`/`refetch` `useEffect` dependency arrays exactly like the existing sort/page state,
  resetting to page 1 on change. `MembersTable`'s `'all'` view gets a Status `<select>`; from
  `AdminUsersTable` gets a Role multi-select. Plain `<input>`/`<select>` with the existing
  `form-input` styling already used throughout (e.g. `MembershipFeesPage.tsx`) — no new shared
  component needed for a page-header addition this small.

## Findings

- **Task 5's code-quality review caught a real bug: the Role filter was a complete no-op over
  real HTTP**, despite passing every test written for it. Two gaps compounded: (1)
  `[ApiController]`'s binding-source inference doesn't treat a bare `IReadOnlyCollection<string>`
  parameter as query-bound without an explicit `[FromQuery]` attribute, so `roles` was never
  populated from the query string at all; (2) axios's default array serialization
  (`roles[]=a&roles[]=b`) doesn't match the repeated-bare-key format ASP.NET Core's query binder
  expects (`roles=a&roles=b`), so even a correctly-bound parameter would have received nothing from
  the frontend as shipped. Every existing test passed anyway, because they all called the
  controller directly, bypassing model binding entirely — none exercised the real HTTP pipeline.
  Fixed in `c0a22ee`: `[FromQuery]` added to the parameter, the shared `apiClient` configured with
  `paramsSerializer: { indexes: null }`, and a genuine HTTP-level test (`AdminControllerHttpTests.cs`,
  new) added specifically to catch this class of bug going forward.

## Not Changed (this round)

- Pending Approval / RMP Verification / Payments tabs — no search/filter added.
- Chapter filter for Members — deferred.
