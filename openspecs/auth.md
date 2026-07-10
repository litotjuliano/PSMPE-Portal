# Auth

## Purpose

Registration and login for the Portal, issuing a JWT the frontend attaches to subsequent
API calls. Backed by ASP.NET Core Identity (`PSMPE.Portal.Domain.Entities.ApplicationUser`).

## Endpoints

- `POST /api/auth/register` — create an account
  - Auth: anonymous
  - Request: `{ email, password, displayName }`
  - Response: `{ token, expiresAt, email, displayName, roles }`
  - New accounts are always granted the `Member` role; `Admin`/`Super Admin`
    must be granted by an existing Super Admin via `POST /api/admin/users/{id}/roles`.
  - TODO: gate behind the seeded `SystemConfig.AllowPublicRegistration` flag once an
    admin settings UI exists to toggle it.

- `POST /api/auth/login` — exchange credentials for a JWT
  - Auth: anonymous
  - Request: `{ email, password }`
  - Response: `{ token, expiresAt, email, displayName, roles }`
  - Returns `401` on invalid credentials.

## Authorization rules

None beyond credential validation — both endpoints are anonymous by design.

## Open questions / TODO

- Refresh token rotation (currently a short-lived access token only, see `Jwt:ExpiryMinutes`).
- Email confirmation / password reset flows (Identity supports these; not wired up yet).
