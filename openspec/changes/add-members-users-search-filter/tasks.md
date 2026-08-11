# Tasks: add-members-users-search-filter

**Goal:** Add server-side search to the Members "All Members" tab (plus a Status filter) and the
Users list (plus a Role filter), so both scale past "page through everything" as row counts grow.

**Architecture:** Small backend additions to two existing paginated queries
(`MemberService.GetAllAsync`, `AdminController.GetUsers`), plus a search input/filter control
added to each page's existing header, following the state/debounce patterns already established
in this codebase.

**Tech Stack:** React 19 + Vite + TypeScript (frontend), .NET 8 + EF Core (backend). No test
runner exists in `apps/web`; the backend has xUnit unit and integration projects.

**Before starting:** read `proposal.md` in this folder.

---

## 1. Backend

- [x] `MemberService.GetAllAsync`: add `string? search` parameter; when non-blank, filter on
      `FirstName`/`LastName`/`MembershipNo`/`User.Email` (case-insensitive `.ToLower().Contains()`).
- [x] `MembersController.GetAll`: accept a `search` query parameter, pass through unchanged.
- [x] `AdminController.GetUsers`: add `string? search` (matches `DisplayName`/`Email`, same style)
      and `IReadOnlyCollection<string>? roles` parameters. Role filtering reuses the existing
      `GetUsersInRoleAsync` → `HashSet<Guid>` → `Where(u => idSet.Contains(u.Id))` pattern already
      in this method (used today to hide Super Admin rows), unioned across the requested roles.
- [x] `GetMembersParams` (`memberApi.ts`) and `GetUsersParams` (`adminApi.ts`): add the new
      `search`/`roles` fields.

## 2. Members "All Members" list UI

- [x] `MembersPage`: add `search`/`status` state, debounced ~350ms, included in the `'all'` tab's
      filter passed to `fetchList`; resets `page` to 1 on change (same as `handleTabChange`).
- [x] `MembersTable`: search input + Status `<select>` in the `'all'` view's card header only.

## 3. Users list UI

- [x] `AdminUsersPage`: add `search`/`roles` state, debounced ~350ms, included in `refetch`;
      resets `page` to 1 on change.
- [x] `AdminUsersTable`: search input + Role multi-select in the card header.

## 4. Tests

- [x] Backend integration tests: `GetAllAsync`/`GetUsers` search matches case-insensitively and
      matches a substring; the Users role filter returns the union of matching roles; existing
      tests still pass unchanged.

## 5. Docs

- [x] `openspecs/members.md` (or wherever `GET /api/members` is documented) and the equivalent
      `GET /api/admin/users` doc — new `search`/`roles` query params.
- [x] Flip this package's `proposal.md` Status to **Implemented** with a short Findings note once
      built and verified, per this repo's existing convention for change packages.

## 5a. Out-of-plan fix: roles query binding

Found during Task 5's code-quality review — see `proposal.md`'s Findings section for the full
story.

- [x] Add `[FromQuery]` to `AdminController.GetUsers`'s `roles` parameter — `[ApiController]`'s
      binding-source inference doesn't treat a bare `IReadOnlyCollection<string>` as query-bound.
- [x] Configure the shared `apiClient` with `paramsSerializer: { indexes: null }` so array params
      serialize as repeated bare keys (`roles=a&roles=b`), matching what the query binder expects,
      instead of axios's bracket-notation default (`roles[]=a&roles[]=b`).
- [x] Add `AdminControllerHttpTests.cs` (new) — a genuine HTTP-level test, since every existing
      test called the controller directly and bypassed model binding entirely.

## 6. Verification

- [x] `dotnet build src/PSMPE.Portal.sln` — **stop the local dev API first**; it locks the output
      DLLs and the build fails with MSB3027 otherwise.
- [x] `dotnet test src/PSMPE.Portal.sln --no-build`.
- [x] `npx tsc -b` and `npx eslint` in `apps/web`.

### Not yet done — needs a running app and a browser

- [ ] Typing in the Members search box narrows the All Members list by name/Membership No./email;
      the Status dropdown filters correctly; both combine with existing sorting/pagination.
- [ ] Typing in the Users search box narrows the list by name/email; the Role filter narrows
      correctly and combines with search.
- [ ] Both reset to page 1 when search/filter changes, and clearing search/filter restores the
      full list.
