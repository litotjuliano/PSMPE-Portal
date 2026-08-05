# Change: Self-Service Account Management and Admin-Triggered Password Reset

## Status

**Approved for implementation.** Designed 2026-08-05.

## Why

No account of any role can edit itself. Verified against the code on 2026-08-05:

- There is **no in-app password change** for anyone. A member who knows their password and simply
  wants a new one must go through the forgot-password email round-trip.
- **No user can edit their own display name.** `displayName` is editable only by an admin, on
  *other* people's records, via `AdminUserFormPage`.
- **The Super Admin account cannot be edited by anyone, including itself.** `PUT
  /api/admin/users/{id}` is gated by `RequireSuperAdmin`, and `AdminController` rejects any Super
  Admin as a *target* (`IsSuperAdminAccountAsync`). Production has exactly one Super Admin, so
  today its display name and password are unchangeable through the application.
- Administrative accounts (Admin, Manager, Accounts, Super Admin) have no `Member` row by design,
  so `/profile` has nothing to offer them. The change shipped earlier today gave them an account
  photo card; useful, but photo alone is not a profile.

Separately, **an Admin cannot help a member back into their account.** Only a Super Admin can set
a password, and only through a full user-record edit that also carries display name and email. The
mechanism is weak on its own terms: the administrator types the new password and passes it to the
member out of band, so an administrator knows a member's password — poor practice on a system
holding member PII, and awkward to defend in an audit.

## What Changes

### 1. New `AccountController` at `/api/account`

The missing third controller alongside `AdminController` (other people's user records) and
`MembersController` (membership data). This one owns "things I may change about my own account".

| Endpoint | Body | Returns |
|---|---|---|
| `PUT /api/account/me` | `{ displayName }` | `{ email, displayName, roles }` |
| `POST /api/account/me/password` | `{ currentPassword, newPassword }` | 204 |

Both `[Authorize]` with no role check — every account has these fields, and restricting them would
mean *adding* code to keep members out of something they need.

**Email is returned but not editable.** It is the login identifier, so changing it stays an
administrative action.

There is deliberately **no `GET /api/account/me`**. The frontend already holds email, display name
and roles from the login response, and `PUT` returns the updated object. Adding a read endpoint
would be a second source of truth to keep consistent, for no current caller.

### 2. Password change rules

`ChangePasswordAsync(user, currentPassword, newPassword)` — Identity verifies the current password;
we do not hand-roll that check. Requiring the current password is what stops a stolen, unexpired
token from being escalated into permanent account takeover.

- Wrong current password → 400 with a generic message that does not indicate which part failed.
- Policy failures reuse `AuthController`'s existing `PasswordPolicyErrorCodes` set, so messages read
  identically to the reset-password flow instead of leaking Identity's raw error codes.
- On success, clear `LockoutEnd` and reset `AccessFailedCount` — matching what `ResetPassword`
  already does. Proving the current password is at least as strong a claim as holding a reset token.
- Failed attempts here do **not** increment the lockout counter. The endpoint already requires a
  valid token, so it is not an anonymous guessing surface, and counting them would let anyone
  holding a stale token lock a member out of their own account. The `global` 300/min limiter applies.

### 3. Admin-triggered password reset

`POST /api/admin/users/{id}/password-reset` in `AdminController`. Generates a token via
`GeneratePasswordResetTokenAsync`, builds the link with the existing `BuildResetPasswordLink` shape,
and sends it through `IEmailSender`. **The administrator never learns the password.**

- Gated by `[RequirePermission(Permissions.Admin.ManageUsers)]`, not a role policy. That permission
  is already seeded to Admin and Super Admin and is editable per-role through the existing roles UI,
  so it can be granted or revoked without a deploy. It is also the gate `CreateUser` uses, keeping
  "can create accounts" and "can help someone back into theirs" together.
- Keeps `IsSuperAdminAccountAsync`: a Super Admin cannot be targeted, so an Admin cannot aim a reset
  at the Super Admin account.
- **Refused for accounts with `EmailConfirmed == false`**, mirroring `ForgotPassword`. Sending a
  reset to an unproven address undermines the reason that rule exists; the admin should use the
  existing `verify-email` action instead.
- **Bypasses `IEmailSendThrottle`.** The per-address cap is 3/hour; counting admin sends against it
  would let a member's own earlier attempts block an administrator who is trying to help them, which
  is the worse failure. The endpoint is authenticated and permission-gated, so it is not an open
  amplifier.
- Logs actor and target via `ILogger`, matching the existing `LogWarning` on rejected Super Admin
  edits.

**This also closes the admin-unlock gap.** `ResetPassword` clears `LockoutEnd` (shipped 2026-08-05),
so an admin-triggered reset is now the recovery path for a locked-out member — which currently
requires `psql` on the droplet (see `add-auth-rate-limiting/tasks.md`, Operational runbook).

### 4. Frontend

| Account | `/profile` shows |
|---|---|
| Administrative (any role other than Member), no Member row | **Account section only** — display name, photo, change password |
| Member | Existing membership wizard/tabs **plus** the Account section |

This supersedes the photo-only card shipped earlier today: `AdminAccountPhotoCard` grows into the
fuller Account section rather than being replaced by a new component. The role branching already in
`MyProfilePage` is unchanged; only the component it renders grows.

`AuthContext` rewrites its cached user from the `PUT` response. Without this the topbar keeps
showing the old display name until the token expires, which reads as "the save didn't work" — the
same class of silent failure this change set already fixed once on `/profile`.

The admin action appears on `/admin/users/:id` as a **"Send password reset email"** button, beside
the existing `verify-email` action, since both are "help this person get into their account".

## Impact

- Affected specs: `account` (**new** capability), `auth` (**modified** — admin-triggered reset)
- Affected code:
  - `WebAPI`: new `AccountController`; `AdminController` gains the reset endpoint
  - `Application`: new account DTOs
  - `Web`: `AdminAccountPhotoCard` grows into an account section, reused for members;
    `AuthContext` cache refresh; `AdminUserFormPage` gains the reset button; new `accountApi`
  - No EF Core migration — `DisplayName` and Identity's password fields already exist
- No changes to existing endpoint response shapes.

## Known Limitation

**Changing a password does not end other sessions.** There are no refresh tokens and no
security-stamp validation, so an already-issued JWT stays valid until `Jwt:ExpiryMinutes` elapses
(default 60). A user who changes their password *because* they suspect compromise keeps the attacker
signed in for up to an hour. This is inherent to the current auth model and is tracked as refresh
token rotation in the backlog; it is recorded here so the gap is visible rather than assumed closed.

Related: the JWT carries `ClaimTypes.Name = DisplayName`, so that claim is stale after a rename
until the token expires. Implementation must confirm nothing server-side reads `ClaimTypes.Name`
for authorization or data access; the frontend reads its cached copy, not the claim.

## Rejected Alternatives

- **Extend `AuthController`.** It is 376 lines already carrying registration, verification, login,
  password reset and consent. Adding a sixth concern makes it the file you scroll to find anything.
  Its `me/data-privacy-consent` endpoints stay put rather than moving — breaking a shipped endpoint
  for tidiness is not worth it, though it does leave account-ish routes split across two prefixes.
- **Extend `MembersController`'s `/api/members/me`.** That is membership data, and administrative
  accounts have no `Member` row — the exact problem being solved.
- **Widen the existing direct password set to Admin.** Simplest, but preserves administrators
  knowing members' passwords. The reset-email path removes that rather than extending it.
- **Move the photo endpoint to `/api/account/me/photo`.** `MemberUpload` is keyed by `UserId`, so
  `/api/members/me/photo` already works for every role despite the name. Moving it means touching
  upload, fetch and six call sites for a naming improvement with no behavioural gain.
