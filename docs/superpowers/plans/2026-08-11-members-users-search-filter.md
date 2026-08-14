# Members/Users Search + Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add server-side free-text search to `GET /api/members` and `GET /api/admin/users`, a
Status filter dropdown on the Members "All Members" tab, and a Role filter on the Users list —
per `openspec/changes/add-members-users-search-filter/proposal.md`.

**Architecture:** Two small backend query additions (`MemberService.GetAllAsync`,
`AdminController.GetUsers`), reusing patterns already in each file (case-insensitive
`.ToLower().Contains()` comparisons, and the existing `GetUsersInRoleAsync` → `HashSet<Guid>`
membership-check pattern for role filtering). Frontend: a debounced search input in each page
component (`MembersPage`, `AdminUsersPage`), with the debounce timer owned by the page, not the
table — tables stay purely presentational, matching how they already receive `page`/`sortBy` as
plain controlled props with no internal fetch-related state.

**Tech Stack:** React 19 + Vite + TypeScript (frontend, no test runner in `apps/web`), .NET 8 +
EF Core (backend, xUnit unit + integration tests).

**Before starting:** read `openspec/changes/add-members-users-search-filter/proposal.md`.
**Stop the local dev API before building** — it locks the output DLLs and `dotnet build` fails
with MSB3027 otherwise (`taskkill //F //IM PSMPE.Portal.WebAPI.exe` or close it from your IDE).

---

### Task 1: Members search — backend

**Files:**
- Modify: `src/PSMPE.Portal.Application/Members/MemberService.cs:90-93` (method signature) and
  after line 131 (filter body)
- Modify: `src/PSMPE.Portal.Application/Members/IMemberService.cs:9-12`
- Modify: `src/PSMPE.Portal.WebAPI/Controllers/MembersController.cs:31-38`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Members/MemberServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `MemberServiceTests.cs` (anywhere among the other `GetAllAsync_*` tests, e.g. right after
`GetAllAsync_WithExcludeUserIds_ExcludesMatchingRowsFromItemsAndTotalCount`, currently ending at
line 398):

```csharp
[Fact]
public async Task GetAllAsync_WithSearch_MatchesNameMembershipNoOrEmail_CaseInsensitively()
{
    using var db = TestDbContext.CreateInMemory();
    var service = new MemberService(db);
    var match = new Member
    {
        UserId = Guid.NewGuid(),
        User = new ApplicationUser { UserName = "maria.santos@example.com", Email = "maria.santos@example.com" },
        MembershipNo = "000042",
        FirstName = "Maria",
        LastName = "Santos",
        Chapter = Chapters.Ncr,
        MemberType = MemberTypes.Regular,
        Status = MembershipStatus.Active,
        SubmittedAt = DateTimeOffset.UtcNow.AddDays(-1),
    };
    var nonMatch = new Member
    {
        UserId = Guid.NewGuid(),
        User = new ApplicationUser { UserName = "pedro.reyes@example.com", Email = "pedro.reyes@example.com" },
        MembershipNo = "000099",
        FirstName = "Pedro",
        LastName = "Reyes",
        Chapter = Chapters.Cebu,
        MemberType = MemberTypes.Regular,
        Status = MembershipStatus.Active,
        SubmittedAt = DateTimeOffset.UtcNow.AddDays(-1),
    };
    db.Members.AddRange(match, nonMatch);
    await db.SaveChangesAsync();

    var byName = await service.GetAllAsync(1, 100, "lastName", "asc", status: null, search: "SANTOS");
    Assert.Single(byName.Items);
    Assert.Equal(match.Id, byName.Items[0].Id);

    var byMembershipNo = await service.GetAllAsync(1, 100, "lastName", "asc", status: null, search: "000042");
    Assert.Single(byMembershipNo.Items);
    Assert.Equal(match.Id, byMembershipNo.Items[0].Id);

    var byEmail = await service.GetAllAsync(1, 100, "lastName", "asc", status: null, search: "maria.santos");
    Assert.Single(byEmail.Items);
    Assert.Equal(match.Id, byEmail.Items[0].Id);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests/PSMPE.Portal.Application.UnitTests.csproj --filter GetAllAsync_WithSearch_MatchesNameMembershipNoOrEmail_CaseInsensitively`
Expected: FAIL to compile — `GetAllAsync` has no parameter named `search` yet.

- [ ] **Step 3: Add the `search` parameter and filter to `MemberService.GetAllAsync`**

In `MemberService.cs`, change the signature at line 90-93 from:

```csharp
    public async Task<PagedResult<MemberDto>> GetAllAsync(
        int page, int pageSize, string sortBy, string sortDir, MembershipStatus? status,
        bool? pendingApprovalOnly = null, bool? pendingPrcVerificationOnly = null,
        IReadOnlyCollection<Guid>? excludeUserIds = null, CancellationToken cancellationToken = default)
```

to:

```csharp
    public async Task<PagedResult<MemberDto>> GetAllAsync(
        int page, int pageSize, string sortBy, string sortDir, MembershipStatus? status,
        bool? pendingApprovalOnly = null, bool? pendingPrcVerificationOnly = null, string? search = null,
        IReadOnlyCollection<Guid>? excludeUserIds = null, CancellationToken cancellationToken = default)
```

Then, right after the `pendingPrcVerificationOnly` filter block (currently ending at line 131,
just before the `var descending = ...` sort line), insert:

```csharp
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Same case-insensitive .ToLower().Contains() idiom already used for MembershipNo
            // comparisons elsewhere in this file (see MembershipNoExistsAsync).
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(m =>
                m.FirstName.ToLower().Contains(normalizedSearch)
                || m.LastName.ToLower().Contains(normalizedSearch)
                || (m.MembershipNo != null && m.MembershipNo.ToLower().Contains(normalizedSearch))
                || (m.User.Email != null && m.User.Email.ToLower().Contains(normalizedSearch)));
        }
```

- [ ] **Step 4: Update `IMemberService.GetAllAsync`'s signature to match**

In `IMemberService.cs`, change lines 9-12 from:

```csharp
    Task<PagedResult<MemberDto>> GetAllAsync(
        int page, int pageSize, string sortBy, string sortDir, MembershipStatus? status,
        bool? pendingApprovalOnly = null, bool? pendingPrcVerificationOnly = null,
        IReadOnlyCollection<Guid>? excludeUserIds = null, CancellationToken cancellationToken = default);
```

to:

```csharp
    Task<PagedResult<MemberDto>> GetAllAsync(
        int page, int pageSize, string sortBy, string sortDir, MembershipStatus? status,
        bool? pendingApprovalOnly = null, bool? pendingPrcVerificationOnly = null, string? search = null,
        IReadOnlyCollection<Guid>? excludeUserIds = null, CancellationToken cancellationToken = default);
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests/PSMPE.Portal.Application.UnitTests.csproj --filter GetAllAsync_WithSearch_MatchesNameMembershipNoOrEmail_CaseInsensitively`
Expected: PASS (3 assertions, one per match kind).

- [ ] **Step 6: Wire `search` through the controller**

In `MembersController.cs`, change `GetAll` (lines 31-38) from:

```csharp
    public async Task<ActionResult<PagedResult<MemberDto>>> GetAll(
        int page = 1, int pageSize = 20, string sortBy = "lastName", string sortDir = "asc",
        MembershipStatus? status = null, bool? pendingApprovalOnly = null, bool? pendingPrcVerificationOnly = null,
        CancellationToken cancellationToken = default)
    {
        var excludeUserIds = await GetSystemAccountUserIdsAsync();
        return Ok(await memberService.GetAllAsync(
            page, pageSize, sortBy, sortDir, status, pendingApprovalOnly, pendingPrcVerificationOnly, excludeUserIds, cancellationToken));
    }
```

to:

```csharp
    public async Task<ActionResult<PagedResult<MemberDto>>> GetAll(
        int page = 1, int pageSize = 20, string sortBy = "lastName", string sortDir = "asc",
        MembershipStatus? status = null, bool? pendingApprovalOnly = null, bool? pendingPrcVerificationOnly = null,
        string? search = null, CancellationToken cancellationToken = default)
    {
        var excludeUserIds = await GetSystemAccountUserIdsAsync();
        return Ok(await memberService.GetAllAsync(
            page, pageSize, sortBy, sortDir, status, pendingApprovalOnly, pendingPrcVerificationOnly, search, excludeUserIds, cancellationToken));
    }
```

- [ ] **Step 7: Build and run the full backend test suite**

Run: `dotnet build src/PSMPE.Portal.sln` — expect 0 errors.
Run: `dotnet test src/PSMPE.Portal.sln --no-build` — expect all passing (326, up from 325).

- [ ] **Step 8: Commit**

```bash
git add src/PSMPE.Portal.Application/Members/MemberService.cs src/PSMPE.Portal.Application/Members/IMemberService.cs src/PSMPE.Portal.WebAPI/Controllers/MembersController.cs tests/PSMPE.Portal.Application.UnitTests/Members/MemberServiceTests.cs
git commit -m "feat: add search to GET /api/members"
```

---

### Task 2: Users search + role filter — backend

**Files:**
- Modify: `src/PSMPE.Portal.WebAPI/Controllers/AdminController.cs:75-121`
- Test: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Admin/AdminControllerTests.cs`

- [ ] **Step 1: Write the two failing tests**

Add to `AdminControllerTests.cs`, near the other `GetUsers_*` tests:

```csharp
[Fact]
public async Task GetUsers_WithSearch_MatchesDisplayNameCaseInsensitively()
{
    var match = await CreateUserAsync(RoleNames.Manager, displayName: "Search Target Alpha");
    await CreateUserAsync(RoleNames.Manager, displayName: "Unrelated Beta");

    var result = UnwrapPaged(await _controller.GetUsers(
        page: 1, pageSize: 1000, search: "search target", cancellationToken: CancellationToken.None));

    Assert.Contains(result.Items, u => u.Id == match.Id);
    Assert.Equal(1, result.TotalCount);
}

[Fact]
public async Task GetUsers_WithRolesFilter_ReturnsUnionOfMatchingRoles()
{
    var manager = await CreateUserAsync(RoleNames.Manager, displayName: "Role Filter Manager");
    var accounts = await CreateUserAsync(RoleNames.Accounts, displayName: "Role Filter Accounts");
    var member = await CreateUserAsync(RoleNames.Member, displayName: "Role Filter Member");

    var result = UnwrapPaged(await _controller.GetUsers(
        page: 1, pageSize: 1000, roles: [RoleNames.Manager, RoleNames.Accounts], cancellationToken: CancellationToken.None));

    Assert.Contains(result.Items, u => u.Id == manager.Id);
    Assert.Contains(result.Items, u => u.Id == accounts.Id);
    Assert.DoesNotContain(result.Items, u => u.Id == member.Id);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests/PSMPE.Portal.WebAPI.IntegrationTests.csproj --filter "GetUsers_WithSearch_MatchesDisplayNameCaseInsensitively|GetUsers_WithRolesFilter_ReturnsUnionOfMatchingRoles"`
Expected: FAIL to compile — `GetUsers` has no `search`/`roles` parameters yet.

- [ ] **Step 3: Add `search` and `roles` parameters and filtering to `AdminController.GetUsers`**

Change the method signature (lines 77-82) from:

```csharp
    public async Task<ActionResult<PagedResult<UserSummaryDto>>> GetUsers(
        int page = 1,
        int pageSize = 20,
        string sortBy = "displayName",
        string sortDir = "asc",
        CancellationToken cancellationToken = default)
    {
```

to:

```csharp
    public async Task<ActionResult<PagedResult<UserSummaryDto>>> GetUsers(
        int page = 1,
        int pageSize = 20,
        string sortBy = "displayName",
        string sortDir = "asc",
        string? search = null,
        IReadOnlyCollection<string>? roles = null,
        CancellationToken cancellationToken = default)
    {
```

Then, right after the existing Super Admin visibility block (currently ending at line 98, just
before `var descending = ...`), insert:

```csharp
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(u =>
                u.DisplayName.ToLower().Contains(normalizedSearch)
                || (u.Email != null && u.Email.ToLower().Contains(normalizedSearch)));
        }

        if (roles is { Count: > 0 })
        {
            // Same shape as the superAdminIds check above: resolve matching ids via
            // UserManager.GetUsersInRoleAsync (a role isn't a queryable column on ApplicationUser),
            // then filter the query by id. Unioned across every requested role.
            var matchingIds = new HashSet<Guid>();
            foreach (var role in roles)
            {
                foreach (var user in await userManager.GetUsersInRoleAsync(role))
                {
                    matchingIds.Add(user.Id);
                }
            }

            query = query.Where(u => matchingIds.Contains(u.Id));
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests/PSMPE.Portal.WebAPI.IntegrationTests.csproj --filter "GetUsers_WithSearch_MatchesDisplayNameCaseInsensitively|GetUsers_WithRolesFilter_ReturnsUnionOfMatchingRoles"`
Expected: PASS.

- [ ] **Step 5: Build and run the full backend test suite**

Run: `dotnet build src/PSMPE.Portal.sln` — expect 0 errors.
Run: `dotnet test src/PSMPE.Portal.sln --no-build` — expect all passing (328, up from 326).

- [ ] **Step 6: Commit**

```bash
git add src/PSMPE.Portal.WebAPI/Controllers/AdminController.cs tests/PSMPE.Portal.WebAPI.IntegrationTests/Admin/AdminControllerTests.cs
git commit -m "feat: add search and role filter to GET /api/admin/users"
```

---

### Task 3: Frontend API types

**Files:**
- Modify: `apps/web/src/core/api/endpoints/memberApi.ts:5-17`
- Modify: `apps/web/src/core/api/endpoints/adminApi.ts:1-2,30-35`

- [ ] **Step 1: Add `search` to `GetMembersParams`**

In `memberApi.ts`, change:

```typescript
export interface GetMembersParams {
  page?: number
  pageSize?: number
  sortBy?: 'lastName' | 'membershipNo' | 'chapter' | 'status' | 'submittedAt'
  sortDir?: 'asc' | 'desc'
  status?: MembershipStatusValue
  /** Applications with no ApprovedAt yet - distinct from status, since an approved
   *  application can still be Status.Pending (approved-but-unpaid). */
  pendingApprovalOnly?: boolean
  /** Members with a proposed PRC License No. change awaiting a decision, or whose current
   *  PRC License No. has never been reviewed at all. */
  pendingPrcVerificationOnly?: boolean
}
```

to:

```typescript
export interface GetMembersParams {
  page?: number
  pageSize?: number
  sortBy?: 'lastName' | 'membershipNo' | 'chapter' | 'status' | 'submittedAt'
  sortDir?: 'asc' | 'desc'
  status?: MembershipStatusValue
  /** Applications with no ApprovedAt yet - distinct from status, since an approved
   *  application can still be Status.Pending (approved-but-unpaid). */
  pendingApprovalOnly?: boolean
  /** Members with a proposed PRC License No. change awaiting a decision, or whose current
   *  PRC License No. has never been reviewed at all. */
  pendingPrcVerificationOnly?: boolean
  /** Matches name, Membership No., or email - case-insensitive substring match. */
  search?: string
}
```

- [ ] **Step 2: Add `search`/`roles` to `GetUsersParams`**

In `adminApi.ts`, add the `Role` type import already present (line 2: `import type { Role } from '../../types/auth'` — unchanged) and change `GetUsersParams` (lines 30-35) from:

```typescript
export interface GetUsersParams {
  page?: number
  pageSize?: number
  sortBy?: 'displayName' | 'email' | 'createdAt' | 'emailConfirmed'
  sortDir?: 'asc' | 'desc'
}
```

to:

```typescript
export interface GetUsersParams {
  page?: number
  pageSize?: number
  sortBy?: 'displayName' | 'email' | 'createdAt' | 'emailConfirmed'
  sortDir?: 'asc' | 'desc'
  /** Matches display name or email - case-insensitive substring match. */
  search?: string
  /** Union of every role listed - a user matching any one of them is included. */
  roles?: Role[]
}
```

- [ ] **Step 3: Typecheck**

Run (from `apps/web`): `npx tsc -b`
Expected: no errors (these are additive optional fields; nothing currently constructs
`GetMembersParams`/`GetUsersParams` exhaustively).

- [ ] **Step 4: Commit**

```bash
git add apps/web/src/core/api/endpoints/memberApi.ts apps/web/src/core/api/endpoints/adminApi.ts
git commit -m "feat: add search/roles fields to Members and Users API param types"
```

---

### Task 4: Members "All Members" list — search + Status filter UI

**Files:**
- Modify: `apps/web/src/integrations/template/pages/MembersTable.tsx`
- Modify: `apps/web/src/core/pages/MembersPage.tsx`

- [ ] **Step 1: Add the new props to `MembersTableProps`**

In `MembersTable.tsx`, add to the `MembersTableProps` interface (after the existing
`canManageMembers: boolean` line):

```typescript
  /** 'all' view only. The raw, un-debounced input value - MembersPage owns the debounce timer
   *  that turns this into the actual filter sent to the server. */
  searchInput?: string
  onSearchInputChange?: (value: string) => void
  statusFilter?: MembershipStatusValue | null
  onStatusFilterChange?: (status: MembershipStatusValue | null) => void
```

Add `MembershipStatusValue` to the existing type-only import from `'../../../core/types/member'`
(currently `import { MembershipStatus } from '../../../core/types/member'` — change to
`import { MembershipStatus, type MembershipStatusValue } from '../../../core/types/member'`).

- [ ] **Step 2: Destructure the new props**

In the `MembersTable` component's parameter list, add `searchInput`, `onSearchInputChange`,
`statusFilter`, `onStatusFilterChange` alongside the existing `canManageMembers`.

- [ ] **Step 3: Render the search box and Status dropdown**

Immediately after the existing card-header `<div>` block (the one containing the `card-title` and
the conditional "New member" button), add a second header row, rendered only for the `'all'`
view:

```tsx
      {view === 'all' && (
        <div className="card-header flex flex-wrap items-center gap-3 border-t border-default-200">
          <input
            type="text"
            className="form-input max-w-xs"
            placeholder="Search by name, membership no., or email…"
            value={searchInput ?? ''}
            onChange={(e) => onSearchInputChange?.(e.target.value)}
          />
          <select
            className="form-input max-w-40"
            value={statusFilter ?? ''}
            onChange={(e) =>
              onStatusFilterChange?.(e.target.value === '' ? null : (Number(e.target.value) as MembershipStatusValue))
            }
          >
            <option value="">All statuses</option>
            <option value={MembershipStatus.Pending}>Pending</option>
            <option value={MembershipStatus.Active}>Active</option>
            <option value={MembershipStatus.Expired}>Expired</option>
            <option value={MembershipStatus.Deactivated}>Deactivated</option>
          </select>
        </div>
      )}
```

- [ ] **Step 4: Add debounced search + status state to `MembersPage`**

In `MembersPage.tsx`, add `MembershipStatusValue` to the existing type import from
`'../types/member'` (currently `import type { Member } from '../types/member'` — change to
`import type { Member, MembershipStatusValue } from '../types/member'`), then add new state
right after the existing `[approving, setApproving]` line:

```typescript
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState<MembershipStatusValue | null>(null)

  // Debounces typing into the search box - fetchList only re-runs off `search`, not
  // `searchInput`, so a fetch fires once per pause in typing rather than per keystroke.
  useEffect(() => {
    const timer = setTimeout(() => {
      setSearch(searchInput)
      setPage(1)
    }, 350)
    return () => clearTimeout(timer)
  }, [searchInput])
```

- [ ] **Step 5: Include `search`/`status` in the request, `'all'` tab only**

Change `fetchList`'s member-fetching line from:

```typescript
      const result = await memberApi.getMembers({ page, pageSize: PAGE_SIZE, sortBy, sortDir, ...activeTab.filter })
```

to:

```typescript
      const result = await memberApi.getMembers({
        page, pageSize: PAGE_SIZE, sortBy, sortDir, ...activeTab.filter,
        ...(tabKey === 'all' && search ? { search } : {}),
        ...(tabKey === 'all' && statusFilter !== null ? { status: statusFilter } : {}),
      })
```

Add `search` and `statusFilter` to `fetchList`'s dependency array (currently
`[page, sortBy, sortDir, queueParam]` on the `eslint-disable-next-line` line below it — change to
`[page, sortBy, sortDir, queueParam, search, statusFilter]`).

- [ ] **Step 6: Reset search/status when switching tabs**

In `handleTabChange`, add `setSearchInput('')` and `setStatusFilter(null)` alongside the existing
`setPage(1)`/`setSortBy(...)`/`setSortDir(...)` calls. (`search` itself will follow via the
debounce effect once `searchInput` changes to `''`, exactly like typing normally would.)

- [ ] **Step 7: Add a `handleStatusFilterChange` handler and pass everything to `MembersTable`**

```typescript
  const handleStatusFilterChange = (status: MembershipStatusValue | null) => {
    setStatusFilter(status)
    setPage(1)
  }
```

In the JSX, add to the `<MembersTable ...>` call:

```tsx
            searchInput={searchInput}
            onSearchInputChange={setSearchInput}
            statusFilter={statusFilter}
            onStatusFilterChange={handleStatusFilterChange}
```

- [ ] **Step 8: Typecheck and lint**

Run (from `apps/web`): `npx tsc -b` and `npx eslint src/integrations/template/pages/MembersTable.tsx src/core/pages/MembersPage.tsx`
Expected: no errors.

- [ ] **Step 9: Commit**

```bash
git add apps/web/src/integrations/template/pages/MembersTable.tsx apps/web/src/core/pages/MembersPage.tsx
git commit -m "feat: add search and status filter to the Members All Members list"
```

---

### Task 5: Users list — search + Role filter UI

**Files:**
- Modify: `apps/web/src/integrations/template/pages/AdminUsersTable.tsx`
- Modify: `apps/web/src/core/pages/AdminUsersPage.tsx`

- [ ] **Step 1: Add the new props to `AdminUsersTableProps`**

In `AdminUsersTable.tsx`, add after the existing `canManageUsers: boolean` line:

```typescript
  /** The raw, un-debounced input value - AdminUsersPage owns the debounce timer. */
  searchInput?: string
  onSearchInputChange?: (value: string) => void
  roleFilter?: Role[]
  onRoleFilterToggle?: (role: Role) => void
```

- [ ] **Step 2: Destructure the new props**

Add `searchInput`, `onSearchInputChange`, `roleFilter`, `onRoleFilterToggle` to the
`AdminUsersTable` component's parameter list.

- [ ] **Step 3: Render the search box and Role filter chips**

Immediately after the existing card-header `<div>` (title + conditional "New user" button), add:

```tsx
      <div className="card-header flex flex-wrap items-center gap-3 border-t border-default-200">
        <input
          type="text"
          className="form-input max-w-xs"
          placeholder="Search by name or email…"
          value={searchInput ?? ''}
          onChange={(e) => onSearchInputChange?.(e.target.value)}
        />
        <div className="flex flex-wrap gap-2">
          {AssignableRoles.map((role) => {
            const active = roleFilter?.includes(role) ?? false
            return (
              <button
                key={role}
                type="button"
                onClick={() => onRoleFilterToggle?.(role)}
                className={`btn btn-sm whitespace-nowrap ${
                  active ? 'bg-primary text-white' : 'border border-default-200 text-default-700'
                }`}
              >
                {role}
              </button>
            )
          })}
        </div>
      </div>
```

(`AssignableRoles` is already imported in this file for the per-row role checkboxes.)

- [ ] **Step 4: Add debounced search + role filter state to `AdminUsersPage`**

In `AdminUsersPage.tsx`, add new state right after the existing `canManageUsers` line:

```typescript
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [roleFilter, setRoleFilter] = useState<Role[]>([])

  useEffect(() => {
    const timer = setTimeout(() => {
      setSearch(searchInput)
      setPage(1)
    }, 350)
    return () => clearTimeout(timer)
  }, [searchInput])
```

- [ ] **Step 5: Include `search`/`roles` in the request**

Change `refetch` from:

```typescript
  const refetch = () =>
    adminApi.getUsers({ page, pageSize: PAGE_SIZE, sortBy, sortDir }).then((result) => {
      setUsers(result.items)
      setTotalCount(result.totalCount)
    })
```

to:

```typescript
  const refetch = () =>
    adminApi
      .getUsers({
        page, pageSize: PAGE_SIZE, sortBy, sortDir,
        ...(search ? { search } : {}),
        ...(roleFilter.length > 0 ? { roles: roleFilter } : {}),
      })
      .then((result) => {
        setUsers(result.items)
        setTotalCount(result.totalCount)
      })
```

Update the `useEffect` that calls `refetch` to depend on `search` and `roleFilter` too — change
`[page, sortBy, sortDir]` to `[page, sortBy, sortDir, search, roleFilter]`.

- [ ] **Step 6: Add a role-filter toggle handler and pass everything to `AdminUsersTable`**

```typescript
  const handleRoleFilterToggle = (role: Role) => {
    setRoleFilter((current) => (current.includes(role) ? current.filter((r) => r !== role) : [...current, role]))
    setPage(1)
  }
```

In the JSX, add to the `<AdminUsersTable ...>` call:

```tsx
            searchInput={searchInput}
            onSearchInputChange={setSearchInput}
            roleFilter={roleFilter}
            onRoleFilterToggle={handleRoleFilterToggle}
```

- [ ] **Step 7: Typecheck and lint**

Run (from `apps/web`): `npx tsc -b` and `npx eslint src/integrations/template/pages/AdminUsersTable.tsx src/core/pages/AdminUsersPage.tsx`
Expected: no errors.

- [ ] **Step 8: Commit**

```bash
git add apps/web/src/integrations/template/pages/AdminUsersTable.tsx apps/web/src/core/pages/AdminUsersPage.tsx
git commit -m "feat: add search and role filter to the Users list"
```

---

### Task 6: Docs and spec status

**Files:**
- Modify: `openspecs/members.md:21-26`
- Modify: `openspecs/roles.md:21-23`
- Modify: `openspec/changes/add-members-users-search-filter/proposal.md` (Status section)
- Modify: `openspec/changes/add-members-users-search-filter/tasks.md` (check off completed items)

- [ ] **Step 1: Document the new `search` query param on `GET /api/members`**

In `openspecs/members.md`, change the `Query:` bullet under `GET /api/members` (lines 21-26) to
add `search` at the end: `` `pendingPrcVerificationOnly` (optional bool — a proposed RMP licence
change awaiting a decision, or one never reviewed at all), `search` (optional — case-insensitive
substring match against first/last name, Membership No., or email) ``.

- [ ] **Step 2: Document the new `search`/`roles` query params on `GET /api/admin/users`**

In `openspecs/roles.md`, add a `Query:` line under the existing `GET /api/admin/users` bullet
(lines 21-23): `` Query: `search` (optional — case-insensitive substring match against display
name or email), `roles` (optional — repeatable, e.g. `?roles=Admin&roles=Manager`; returns the
union of matching roles) ``.

- [ ] **Step 3: Flip the change package's Status to Implemented**

In `openspec/changes/add-members-users-search-filter/proposal.md`, change the `## Status` section
from `**Proposed.** ...` to `**Implemented.** ...`, following the exact style of the other change
packages in `openspec/changes/` (e.g. `consolidate-member-admin-lists/proposal.md`) — state the
day, the backend build/test result, and flag that the live-browser pass is what's still unverified
(see `tasks.md`).

- [ ] **Step 4: Check off completed items in `tasks.md`**

Mark every checkbox done in Tasks 1-6 of this plan as `[x]` in the corresponding items of
`openspec/changes/add-members-users-search-filter/tasks.md`. Leave the "Not yet done — needs a
running app and a browser" section unchecked.

- [ ] **Step 5: Commit**

```bash
git add openspecs/members.md openspecs/roles.md openspec/changes/add-members-users-search-filter/
git commit -m "docs: document Members/Users search and filter params, mark change implemented"
```

---

### Task 7: Final verification

- [ ] **Step 1: Full backend build and test**

Run: `dotnet build src/PSMPE.Portal.sln` — expect 0 warnings, 0 errors.
Run: `dotnet test src/PSMPE.Portal.sln --no-build` — expect all passing (328, up from 325 at the
start of this plan).

- [ ] **Step 2: Full frontend typecheck, lint, build**

Run (from `apps/web`): `npx tsc -b`, `npx eslint .`, `npm run build`.
Expected: no errors (only any pre-existing warnings noted in prior change packages).

- [ ] **Step 3: Manual browser check — Members**

Start the app, log in as Admin. On the Members "All Members" tab: type a partial name into the
search box and confirm the list narrows after a short pause; select each Status option and
confirm the list filters accordingly; confirm both combine (search + status together) and that
clearing the search box restores the full list. Confirm the Pending Approval / RMP Verification /
Payments tabs are unchanged (no search/filter controls, exactly as before).

- [ ] **Step 4: Manual browser check — Users**

On the Users list: type a partial name/email into the search box and confirm the list narrows;
toggle one or more Role chips and confirm the list narrows to the union of selected roles; confirm
search and role filter combine correctly; confirm clearing both restores the full list.
