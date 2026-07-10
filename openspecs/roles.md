# Roles & Permissions

## Purpose

The Portal serves a plumbing trade organization, so its role set names the organization's
actual roles rather than generic CMS terms: `Super Admin`, `Admin`, `Manager`, `Accounts`,
`Member`. Roles are still ASP.NET Core Identity `IdentityRole<Guid>` records (no custom role
entity), but what each role can *do* is no longer hardcoded — it's a set of **permission**
claims (claim type `"permission"`, e.g. `content:create`) stored on the role via Identity's
built-in `AspNetRoleClaims` table and editable by a Super Admin through `/admin/roles`,
without a code change or deployment.

This supersedes/extends the inline role-assignment description in `auth.md`
(`POST /api/admin/users/{id}/roles`) by adding a mirrored `DELETE` endpoint and the permission
layer described below. It also resolves the open TODO in `ai-prompt-execution.md`
("restrict by role") — `POST /api/ai/prompt` is now gated by the `ai:use-prompt` permission
via `[RequirePermission]`.

## Endpoints

- `GET /api/admin/users` — list users with their roles (unchanged, documented in `auth.md`/here for completeness)
  - Auth: `RequireAdmin` policy (Admin or Super Admin role)
- `POST /api/admin/users/{id}/roles` — assign a role to a user
  - Auth: `RequireSuperAdmin` policy
  - Request: `{ role }`
- `DELETE /api/admin/users/{id}/roles` — remove a role from a user
  - Auth: `RequireSuperAdmin` policy
  - Request: `{ role }` (body-based, mirrors the `POST` shape — avoids URL-encoding role names with spaces)
  - Refuses to remove `Super Admin` from the last remaining Super Admin account (`400`)
- `GET /api/admin/roles` — list all roles with their current permission claims
  - Auth: `RequireAdmin` policy
  - Response: `[{ id, name, permissions }]`
- `PUT /api/admin/roles/{roleId}/permissions` — replace a role's permission set
  - Auth: `RequireSuperAdmin` policy
  - Request: `{ permissions }` — diffed against current claims; unknown permission values return `400`
- `GET /api/admin/permissions` — list every defined permission constant
  - Auth: `RequireAdmin` policy
  - Lets the frontend render permission checkboxes without hardcoding the list

## Authorization rules

- **Roles** (fixed set, `Domain.Enums.RoleNames`): `Super Admin`, `Admin`, `Manager`,
  `Accounts`, `Member`. New self-registrations always get `Member` (see `auth.md`).
- **Permissions** (`Domain.Enums.Permissions`, `resource:action` naming): `content:create`,
  `content:update`, `content:delete`, `content:manage-others`, `layout:create`,
  `layout:delete`, `layout:delete-system`, `admin:manage-users`, `admin:manage-roles`,
  `ai:use-prompt`.
- Permission claims are embedded in the JWT alongside role claims at login/register
  (`JwtTokenGenerator`), so `[RequirePermission(...)]` checks (`PermissionAuthorizationHandler`)
  are pure claim lookups with no DB round-trip per request.
- `content:manage-others` is an *additional* bypass on top of the existing
  `Admin`/`Super Admin` role check in `OwnershipAuthorizationHandler` — a non-admin role could
  be granted it to manage others' content without being made a full Admin.
- `layout:delete-system` replaces what used to be a hardcoded `IsInRole(SuperAdmin)` check in
  `LayoutService.DeleteAsync`. Seeded only to `Super Admin` by default, so out-of-the-box
  behavior is unchanged.

### Default permission grants (seeded on first run, editable afterward)

| Role | Grants |
|---|---|
| Super Admin | All permissions |
| Admin | Content: create/update/delete/manage-others; Layout: create/delete; Admin: manage-users; Ai: use-prompt |
| Manager | Content: create/update/delete; Layout: create; Ai: use-prompt |
| Accounts | Content: update; Ai: use-prompt |
| Member | Content: create/update |

Grants are applied by `IdentitySeeder` **only** the first time a role is created — re-running
the seeder never clobbers permissions a Super Admin edits later via `/admin/roles`.

## Open questions / TODO

- **Accounts role is intentionally minimal.** No dues/billing domain exists yet (no `Invoice`,
  `Dues`, or membership-payment entities — only `ContentItem`/`Layout`/`ApplicationUser`/
  `SystemConfig`). Real Accounts capability (view member dues status, record payments) needs
  its own feature with its own entities and permissions; this iteration only ensures the role
  exists and has a safe, minimal default.
- Role CRUD (creating/deleting custom roles beyond the fixed 5) is out of scope — roles stay a
  fixed set for now; only their permissions are editable.
- Per-permission frontend UI gating is not implemented — route/nav visibility stays role-based
  (`ProtectedRoute`, `AppMenu.filterByRole`); only the backend enforces permissions granularly.
- No audit log for role/permission changes yet (same gap noted for `POST /api/admin/users/{id}/roles`
  in `auth.md`).
