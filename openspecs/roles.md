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
  - Auth: `RequireAdminOrApproval` policy (Admin, Super Admin, or Approval role — Approval is
    view-only here, since every write endpoint below stays on `RequireAdmin`/`RequireSuperAdmin`)
- `GET /api/admin/users/{id}` — get one user
  - Auth: `RequireAdminOrApproval` policy
- `POST /api/admin/users` — create a login account (the admin "New user" form)
  - Auth: `admin:manage-users` permission — the one action in this whole family still gated by a
    configurable permission claim rather than a hard role check (see the narrowed-scope note
    below)
  - Request: `{ email, displayName, password, role? }` — `role` defaults to `Member`; assigning
    any role other than `Member` additionally requires the caller to hold the `Super Admin`
    *role* itself, not just this permission (mirrors the hard gate on `AssignRole`/`RemoveRole`
    below, so this endpoint can't be used to grant privilege an Admin couldn't already grant
    directly). `Super Admin` itself can never be assigned through this endpoint, for any caller
    (`403`).
- `PUT /api/admin/users/{id}` — edit a user's display name/email, optionally reset their password
  - Auth: `RequireSuperAdmin` policy — a regular Admin cannot edit any user's account, even one
    granted `admin:manage-users` (unlike `POST` above, this can't be re-delegated by editing role
    permissions - it's a hard role check)
  - Request: `{ displayName, email, newPassword? }`
- `DELETE /api/admin/users/{id}` — permanently delete a login account
  - Auth: `RequireSuperAdmin` policy, same reasoning as `PUT` above
  - Cascades: deletes the linked `Member` profile in full, if one exists (`Cascade` FK — see
    `members.md`)
  - `409` if that member has any RMP/PRC verification history on record (`Restrict` FK on
    `PrcVerificationHistories`) — a clean rejection instead of a raw `DbUpdateException`
  - Purges the user's `MemberUploads`/`MemberCertificates` rows *and* their backing files first —
    neither has an FK relationship to `Member` at all (see `members.md`), so they'd otherwise be
    silently orphaned once the cascade above removes the `Member` row
  - `400` targeting your own account; `404` (hidden) or `403` targeting a Super Admin account
  - Frontend: `/admin/users` hides the Edit/Delete icons entirely (not just disables them) for a
    non-Super-Admin caller — leaving only the Email Verification action below on that row
- `POST /api/admin/users/{id}/verify-email` — manually mark a user's email verified, without them
  clicking the confirmation link
  - Auth: `RequireAdmin` policy — the one action a regular Admin still has on another user's row
    now that `PUT`/`DELETE` above are Super-Admin-only
- `POST /api/admin/users/{id}/roles` — assign a role to a user
  - Auth: `RequireSuperAdmin` policy
  - Request: `{ role }`
- `DELETE /api/admin/users/{id}/roles` — remove a role from a user
  - Auth: `RequireSuperAdmin` policy
  - Request: `{ role }` (body-based, mirrors the `POST` shape — avoids URL-encoding role names with spaces)
  - Refuses to remove `Super Admin` from the last remaining Super Admin account (`400`)
- `GET /api/admin/roles` — list all roles with their current permission claims
  - Auth: `RequireAdminOrApproval` policy
  - Response: `[{ id, name, permissions }]`
- `PUT /api/admin/roles/{roleId}/permissions` — replace a role's permission set
  - Auth: `RequireSuperAdmin` policy
  - Request: `{ permissions }` — diffed against current claims; unknown permission values return `400`
- `GET /api/admin/permissions` — list every defined permission constant
  - Auth: `RequireAdminOrApproval` policy
  - Lets the frontend render permission checkboxes without hardcoding the list

## Authorization rules

- **Roles** (fixed set, `Domain.Enums.RoleNames`): `Super Admin`, `Admin`, `Manager`,
  `Accounts`, `Approval`, `Member`. New self-registrations always get `Member` (see `auth.md`).
- **Permissions** (`Domain.Enums.Permissions`, `resource:action` naming): `content:create`,
  `content:update`, `content:delete`, `content:manage-others`, `layout:create`,
  `layout:delete`, `layout:delete-system`, `admin:manage-users`, `admin:manage-roles`,
  `ai:use-prompt`, `members:view`, `members:manage`, `members:approve`.
- `members:approve` covers exactly the membership-approval pipeline: RMP/PRC verification
  approve/reject, the Membership ID availability check, the final approve call, and uploading a
  walk-in's payment proof during approval. It is granted *in addition to* `members:manage` on
  those 5 endpoints (`[RequirePermission(Permissions.Members.Manage, Permissions.Members.Approve)]`
  — the caller needs only one of the two), not a replacement for it, so `Admin`'s existing grant
  is unaffected. It exists so the `Approval` role can run that one workflow without also getting
  member create/update/delete or payment verify/reject (both still `members:manage`-only).
- `[RequirePermission(...)]` accepts more than one permission and succeeds if the caller holds
  any of them (`PermissionRequirement`/`PermissionAuthorizationHandler`) — an OR, not an AND.
- Permission claims are embedded in the JWT alongside role claims at login/register
  (`JwtTokenGenerator`), so `[RequirePermission(...)]` checks (`PermissionAuthorizationHandler`)
  are pure claim lookups with no DB round-trip per request.
- `content:manage-others` is an *additional* bypass on top of the existing
  `Admin`/`Super Admin` role check in `OwnershipAuthorizationHandler` — a non-admin role could
  be granted it to manage others' content without being made a full Admin.
- `layout:delete-system` replaces what used to be a hardcoded `IsInRole(SuperAdmin)` check in
  `LayoutService.DeleteAsync`. Seeded only to `Super Admin` by default, so out-of-the-box
  behavior is unchanged.
- `admin:manage-users` only gates account *creation* (`POST /api/admin/users`) — editing or
  deleting an existing user account requires the `Super Admin` role outright (see Endpoints
  above). Granting this permission to a non-Super-Admin role lets them create new accounts but
  never edit or delete existing ones; this was tightened from an earlier version where the same
  permission also gated edit/delete, which let a regular Admin freely edit/delete any user.

### Default permission grants (seeded on first run, editable afterward)

| Role | Grants |
|---|---|
| Super Admin | All permissions |
| Admin | Content: create/update/delete/manage-others; Layout: create/delete; Admin: manage-users; Ai: use-prompt; Members: view, manage |
| Manager | Content: create/update/delete; Layout: create; Ai: use-prompt; Members: view |
| Accounts | Content: update; Ai: use-prompt; Members: view |
| Approval | Members: view, approve |
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
