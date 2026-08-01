# Auth

## Purpose

Registration and login for the Portal, issuing a JWT the frontend attaches to subsequent
API calls. Backed by ASP.NET Core Identity (`PSMPE.Portal.Domain.Entities.ApplicationUser`).

## Endpoints

- `POST /api/auth/register` — create an account
  - Auth: anonymous
  - Request: `{ email, password, displayName, username?, dataPrivacyConsent }`
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
  - `dataPrivacyConsent` must be `true` (RA 10173) — see "Data privacy consent" below.
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

- `POST /api/auth/forgot-password` — request a password reset link
  - Auth: anonymous
  - Request: `{ email }`
  - Response: always `200` with a generic `{ message, devResetLink? }`, regardless of whether the
    email exists or is verified — same anti-enumeration approach as `resend-verification-email`.
    An unverified account is treated the same as a nonexistent one (no reset link before the email
    is even confirmed).

- `POST /api/auth/reset-password` — consume a reset link and set a new password
  - Auth: anonymous
  - Request: `{ userId, token, newPassword }` (both `userId`/`token` come from the emailed link)
  - Response: `{ message }` on success — **not** a JWT; unlike `verify-email` this does not
    auto-login, the user signs in normally afterward at `/login`. `400` with a generic
    invalid-link message if the token itself is invalid/expired/already used; `400`
    (`ValidationProblem`) with field errors if the new password fails Identity's password policy.

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

## Password reset

Same shape as email verification, reusing Identity's built-in
`GeneratePasswordResetTokenAsync`/`ResetPasswordAsync` (already used by the admin-triggered reset
in `AdminController.UpdateUser`) and the same `IEmailSender`/dev-link pattern.

- Frontend: `ForgotPasswordPage` (`/forgot-password`, public) collects an email and calls
  `forgot-password`, showing the generic message and the dev-only link when present.
  `ResetPasswordPage` (`/reset-password`, public) reads `?userId&token` from the emailed link,
  collects and confirms a new password, and calls `reset-password`; on success it redirects to
  `/login` with a success message rather than auto-authenticating.

- `GET /api/auth/me/data-privacy-consent` — where the caller stands on the notice
  - Auth: authenticated (resolves the user from the JWT; there's nothing to authorize beyond that,
    since an account can only read its own consent)
  - Response: `{ needsConsent, currentVersion, consentedVersion?, consentedAt? }`

- `POST /api/auth/me/data-privacy-consent` — record consent at the current wording
  - Auth: authenticated
  - Request: **no body** — consent is always to the server's current version at the server's
    clock. Accepting a version from the caller would let them claim consent to text they never saw.
  - Response: the same status shape, now satisfied.

## Data privacy consent

Public sign-up requires consent to the Association's data privacy notice (RA 10173). The sign-up
form shows the wording with a link to `privacy.gov.ph`, but the **enforcement is server-side**:
`Register` rejects the request with a `400` before touching anything else when
`dataPrivacyConsent` isn't `true`, so a direct API call can't skip it and no partial account is
left behind. The field defaults to `false`, so an older client that omits it fails closed rather
than silently registering without consent.

On success the account records **when** and **to what**:

- `ApplicationUser.DataPrivacyConsentAt` — stamped from the server clock, never from the request,
  so the caller can't backdate it.
- `ApplicationUser.DataPrivacyConsentVersion` — `AuthController.DataPrivacyConsentVersion`, a
  constant tracking the revision of the wording. **Bump it whenever the consent text in
  `RegisterPage.tsx` changes**, otherwise a wording change silently reinterprets old consent as
  agreement to new terms. The version comes from the constant rather than the request for the
  same reason the timestamp does.

Both are nullable and set together. **Null means "no consent on record", not "refused"** — it's
the expected state for accounts that never went through public registration (seeded accounts,
`AdminController`-created ones) and for anyone who registered before this shipped. There is no
backfill; treat existing rows as unknown rather than consented.

### Re-consent when the wording changes

`DataPrivacyConsent.CurrentVersion` (`Domain.Enums`) is the single source of truth, and
`NeedsConsent(consentedVersion)` is just `consentedVersion != CurrentVersion` — so a null (never
consented) account needs consent too.

**Bumping `CurrentVersion` is the mechanism**: it immediately makes every existing account's
consent stale, and each user is asked to re-accept on their next page load. The consent *text*
lives in one place on the frontend (`core/constants/dataPrivacyConsent.tsx`, shared by the sign-up
form and the gate) precisely because one version string is stamped against whoever accepts — two
drifting copies would record the same version against two different texts. **Change the text and
the constant in the same commit.**

- Frontend: `DataPrivacyConsentGate` (`core/auth`) sits between `ProtectedRoute` and `AppShell` as
  its own layout route — outside `AppShell` so the nested admin `ProtectedRoute`s don't re-run the
  check on every navigation. It renders the app normally and overlays a modal when
  `needsConsent`. It's a prompt, not a lockout: accepting clears it immediately and "Sign out" is
  always offered.
- **Fails open.** If the status call errors, the gate lets the user through. A transient 500 would
  otherwise lock every user out of the entire portal, which is worse than briefly serving someone
  whose re-consent is outstanding — they're prompted again on the next load.
- Admin visibility: `UserSummaryDto` carries `dataPrivacyConsentAt`/`dataPrivacyConsentVersion`,
  surfaced as a "Data Privacy" column on `/admin/users` (Consented, with version + timestamp on
  hover, vs. "No record"). `POST /api/admin/users` leaves both null — an admin creating an account
  can't consent on that person's behalf.

## Open questions / TODO

- Refresh token rotation (currently a short-lived access token only, see `Jwt:ExpiryMinutes`).
- Consent history is last-write-wins: re-consenting overwrites the previous timestamp/version
  rather than appending. Enough to answer "does this user hold current consent"; not enough to
  reconstruct *when* they agreed to a superseded version. Needs a separate consent-events table
  if the Association ever has to show that.
