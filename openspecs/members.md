# Members

## Purpose

A PSMPE professional membership profile — distinct from the `Users` concept in `auth.md`/
`roles.md`. `ApplicationUser` is the login/role account (shared by staff roles like Admin/
Manager/Accounts too); `Member` is specifically a professional-membership record (PRC license,
membership number, chapter, dues, renewal date). Every `Member` has exactly one linked
`ApplicationUser` (1:1, required, enforced by a unique index on `UserId`), but not every
`ApplicationUser` has a `Member` profile.

Sourced from the real product spec (`psmpe web portal.pptx`), scoped down for this iteration —
see Open questions/TODO for what's deferred.

## Endpoints

- `GET /api/members` — paged/sorted list of member profiles
  - Auth: `members:view` permission (deliberately *not* also gated by the `RequireAdmin` role
    policy — that would block Manager/Accounts, who are granted `members:view` but aren't
    Admin/Super Admin by role; see Authorization rules)
  - Query: `page`, `pageSize`, `sortBy` (`lastName` | `membershipNo` | `chapter` | `status`), `sortDir`
  - Response: `PagedResult<MemberDto>`
- `GET /api/members/{id}` — get one member profile
  - Auth: `members:view` permission
- `GET /api/members/me` — the caller's own member profile
  - Auth: authenticated (any role)
  - Returns `404` if the caller hasn't completed their profile yet
- `POST /api/members` — admin creates a member profile for an existing user
  - Auth: `members:manage` permission
  - Request: `{ userId, membershipNo, firstName, middleName, lastName, suffix, birthdate, gender, address, prcLicenseNo, chapter, company, renewalDueDate, nationalDuesReferenceNo }`
  - Does **not** create a login account — that's `POST /api/auth/register`'s job. `400` if
    `userId` doesn't exist; `409` if that user already has a profile or `membershipNo` collides.
  - Always starts as `Status: Pending`.
- `PUT /api/members/{id}` — admin edit, including `Status`
  - Auth: `members:manage` permission
- `PUT /api/members/me` — self-service edit of the caller's own profile
  - Auth: authenticated (any role)
  - Request: same shape as create, minus `userId`/`membershipNo`/`Status` — those are
    business-controlled, not self-service
  - **Upserts**: creates the profile (with a server-generated `MembershipNo`, `Status: Pending`)
    if the caller doesn't have one yet — this is how "complete your profile after registering"
    works, since self-registration only creates the login + `Member` role today, not the profile.
- `DELETE /api/members/{id}` — admin removes just the member profile
  - Auth: `members:manage` permission
  - Leaves the underlying login/role account intact — retiring someone's membership record
    doesn't delete their system account.

## Authorization rules

- `members:view` / `members:manage` permissions (see `roles.md`), seeded by default to Admin
  (both) and Manager/Accounts (view only) — editable afterward via `/admin/roles` like any other
  permission.
- Self-service (`/me` endpoints) requires no permission claim, only authentication — anyone can
  view/edit their *own* profile once linked, roles/permissions only gate viewing/editing *other*
  people's profiles.

## Open questions / TODO

- **File uploads deferred**: `PhotoUrl`/`PrcIdUrl` exist as nullable columns but there's no
  upload endpoint or storage backend yet (no file-upload infrastructure exists anywhere in this
  codebase today). Values must currently be set via a hosted URL if populated at all.
- **Chapter is a fixed const list** (`Domain.Enums.Chapters`, mirrors `RoleNames`/`Permissions`'s
  style), not a database-editable entity — no mockup or requirement showed chapter CRUD.
  Revisit as a real entity+table if admins ever need to add/rename chapters without a deploy.
- **Registration wizard deferred**: the real product spec shows a 4-step registration flow
  (Personal → Contact → Account → Additional info) that creates both the login and the profile
  together. Self-registration today stays the existing single simple form; the Member profile is
  filled in afterward via `/profile` (self-service) or by an admin.
- **`MembershipNo` auto-generation is not race-safe**: `MemberService`'s self-service upsert path
  generates a sequential zero-padded number from the current row count. The unique index
  guarantees no duplicate is ever persisted, but a concurrent collision would surface as a
  `SaveChanges` failure rather than a graceful retry — acceptable at current scale, revisit if
  registration volume grows.
- No audit log for profile/status changes yet (same gap noted for role changes in `roles.md`).
