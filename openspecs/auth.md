# Auth

## Purpose

Registration and login for the Portal, issuing a JWT the frontend attaches to subsequent
API calls. Backed by ASP.NET Core Identity (`PSMPE.Portal.Domain.Entities.ApplicationUser`).

## Endpoints

- `POST /api/auth/register` — create an account
  - Auth: anonymous
  - Request: `{ email, password, displayName, username? }`
  - Response: `{ email, message, devVerificationLink? }` — **not** a JWT. The account exists but
    can't be used until the email is confirmed (see "Email verification" below).
  - `username` is optional — omitting it preserves the original behavior of `UserName` mirroring
    `Email`. If provided, `409` if already taken. The frontend's `/register` sign-up form collects
    it with a live check backed by `GET /api/auth/username-available`.
  - New accounts are always granted the `Member` role; `Admin`/`Super Admin`
    must be granted by an existing Super Admin via `POST /api/admin/users/{id}/roles`.
  - This is intentionally basic sign-up only — no Member profile is created here. The full
    membership application (Personal/Contact/Account/Additional Info) is a separate, resumable
    wizard completed afterward from `/profile`; see `members.md`'s "Registration: simple sign-up
    now, resumable application wizard later" section. Auth stays unaware of Members either way
    (no backend coupling).
  - TODO: gate behind the seeded `SystemConfig.AllowPublicRegistration` flag once an
    admin settings UI exists to toggle it.

- `GET /api/auth/username-available?username=...` — live availability check
  - Auth: anonymous
  - Response: `bool` (`true` if no account currently uses that username)

- `POST /api/auth/login` — exchange credentials for a JWT
  - Auth: anonymous
  - Request: `{ email, password }`
  - Response: `{ token, expiresAt, email, displayName, roles }`
  - Returns `401` on invalid credentials, or `403` with `{ message, code: "EMAIL_NOT_CONFIRMED" }`
    if the account exists, the password is correct, but the email hasn't been verified yet —
    distinct from `401` so the frontend can show a "Resend verification email" action specifically
    instead of a plain "wrong credentials" message.

- `POST /api/auth/verify-email` — confirm an account's email
  - Auth: anonymous
  - Request: `{ userId, token }` (both come from the link the account owner was emailed)
  - Response: a real `{ token, expiresAt, email, displayName, roles }` on success (auto-login —
    the user lands in the app directly rather than having to log in again separately). `400` if
    the token is invalid/expired/already used.

- `POST /api/auth/resend-verification-email` — request a new verification link
  - Auth: anonymous
  - Request: `{ email }`
  - Response: always `200` with a generic `{ message, devVerificationLink? }`, regardless of
    whether the email exists or is already verified — avoids leaking account existence.

## Email verification

Registration requires verifying the email address before the account can be used — **required**,
not skippable. Uses ASP.NET Core Identity's built-in support directly
(`UserManager.GenerateEmailConfirmationTokenAsync`/`ConfirmEmailAsync`,
`AddDefaultTokenProviders()` already registered) - no custom token logic.

- **`IEmailSender`** (`Application.Common.Interfaces`) abstracts *how* the email actually gets
  sent (same pattern as `IFileStorageService`). `DependencyInjection.AddInfrastructure` picks the
  implementation based on config: **`SmtpEmailSender`** (MailKit) when `Smtp:Host` is set, else
  **`ConsoleEmailSender`** (just logs via `ILogger`) so local dev works without real credentials.
- Regardless of which `IEmailSender` is active, **the verification link is also returned directly
  in API responses** (`Register`, `resend-verification-email`) whenever `!env.IsProduction()` -
  covering Development *and* the `Testing` environment `CustomWebApplicationFactory` uses, so the
  whole flow is exercisable in tests without a real inbox.
- The link points at the **frontend**, not the API (`{Frontend:BaseUrl}/verify-email?userId=...
  &token=...`, URL-encoded) - `Frontend:BaseUrl` is a new config key (`appsettings.json`/`.env`,
  default `http://localhost:5173`), since the API needs to know the frontend's origin to build it.
- Frontend: `VerifyEmailPage` (`/verify-email`, public) handles two states - landed right after
  registering (no query params: shows "check your email" + a resend action + the dev-only link
  when present), or landed via the emailed link (`?userId&token` present: auto-calls
  `verifyEmail`, then redirects into the app on success).
- **Known, low-stakes side effect**: any account self-registered before this shipped has
  `EmailConfirmed = false` by Identity's default and can no longer log in until confirmed -
  re-registering (or an admin manually confirming it) is an acceptable fix for what were only
  test accounts. Seeded/admin-created accounts (`IdentitySeeder.cs`, `AdminController.cs`) already
  hardcode `EmailConfirmed = true` and are unaffected.

## Authorization rules

None beyond credential validation — all endpoints are anonymous by design.

## Open questions / TODO

- Refresh token rotation (currently a short-lived access token only, see `Jwt:ExpiryMinutes`).
- Password reset flow (Identity supports this too; not wired up yet - same shape as email
  verification, would reuse `IEmailSender`).
