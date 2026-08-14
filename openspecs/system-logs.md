# System Logs

## Purpose

Before this feature, two categories of event had no persistent record at all: security-relevant
actions (rate-limit rejections, account lockouts, email-throttle blocks, membership approvals) and
application errors (unhandled backend exceptions, uncaught frontend errors). Diagnosing a lockout
complaint or a spike in 500s meant grepping container logs on the droplet, if they hadn't already
rotated out. `AuditLog` and `ErrorLog` are two new append-only Postgres tables plus a Super-Admin-
only `/admin/system-logs` page (Audit and Errors tabs) that gives that history a queryable home.

The two tables are deliberately separate, not one generic "events" table: an audit event is a
*decision the system made* (reject this request, lock this account, approve this member) and is
retained for accountability; an error is *something going wrong* and is retained only long enough
to be useful for debugging. Mixing them would force one retention policy on both.

## Endpoints

- `GET /api/admin/audit-log` — paged list of audit events
  - Auth: `RequireSuperAdmin` policy — stricter than the general `RequireAdminOrApproval` gate used
    by most of `/api/admin/*` (see `roles.md`), matching how role assignment and user deletion are
    already Super-Admin-only in `AdminController`
  - Query: `page`, `pageSize` (default 20), `search` (optional), `eventType` (optional, exact match
    against values like `auth.rate_limit.rejected`, `auth.account.locked_out`,
    `auth.email_throttle.blocked`, `membership.approved`), `from`/`to` (optional `DateTimeOffset`
    range on `CreatedAt`)
  - Response: `PagedResult<AuditLogEntryDto>` — `{ id, eventType, actorUserId, actorEmail, actorIp,
    targetType, targetId, metadata, createdAt }`. `actorEmail` is resolved server-side with a single
    batched `UserManager` query per page (same pattern as `AdminController.GetUsers`'s role
    resolution), not a per-row lookup.
- `GET /api/admin/error-log` — paged list of recorded errors
  - Auth: `RequireSuperAdmin` policy
  - Query: `page`, `pageSize` (default 20), `search` (optional), `source` (optional `ErrorSource`
    enum filter — `Backend` | `Frontend`), `from`/`to` (optional `DateTimeOffset` range on
    `CreatedAt`)
  - Response: `PagedResult<ErrorLogEntryDto>` — `{ id, source, exceptionType, message, stackTrace,
    requestPath, requestMethod, url, userId, userEmail, userAgent, metadata, createdAt }`.
    `requestPath`/`requestMethod` are only populated for `Backend` rows; `url` only for `Frontend`
    rows. `userEmail` is resolved the same batched way as `actorEmail` above.
- `POST /api/errors/frontend` — records a frontend-reported error
  - Auth: none (`[Authorize]` deliberately omitted) — a frontend error can happen before login (the
    login page itself, a token refresh failure), so this endpoint must be reachable unauthenticated.
    It still records the caller's identity when a valid token *is* present, read via
    `ICurrentUserService` off the same JWT middleware that populates it for every request regardless
    of whether the endpoint requires it.
  - Rate limited: 30 requests / 5 minutes per IP (`RateLimitingServiceExtensions.ErrorReportPolicy`,
    the `"error-report"` fixed-window policy) — necessarily unauthenticated and accepting free-text
    payloads, exactly what the rate-limiting layer exists to protect. A rejection here is itself an
    audited event (`auth.rate_limit.rejected`, `policy: "error-report"` in `Metadata`), same as any
    other rate-limited endpoint.
  - Request: `{ message, stackTrace?, url?, componentStack? }`. `componentStack` (React's
    per-component error trace) is capped to 4000 characters server-side before being embedded in
    `Metadata` as JSON — `ErrorLog.Metadata` is an unbounded text column, and this endpoint is public,
    so the cap exists to bound worst-case row size independent of the rate limit's own window.
  - Response: `204 No Content`

## Authorization rules

- Both GET endpoints require the `RequireSuperAdmin` policy, not `RequireAdminOrApproval`. A
  regular Admin can manage users, content, and members but cannot see the audit trail or error log
  — this mirrors the existing Super-Admin-only gate on user edit/delete and role assignment
  (`roles.md`), on the theory that "who did what, and what broke" is itself sensitive.
- `POST /api/errors/frontend` is intentionally the one open door in this feature — see Endpoints
  above. It writes exactly one row per call and never returns any existing log data, so opening it
  up doesn't leak anything the GET endpoints protect.
- The frontend route `/admin/system-logs` is additionally gated client-side by `ProtectedRoute`
  with `requiredRoles={[Roles.SuperAdmin]}` (`core/routes/router.tsx`) and hidden from the side nav
  for anyone else — belt-and-suspenders on top of the server-side policy, matching the existing
  pattern for other Super-Admin-only pages.

## What gets audited, and what doesn't

- **`auth.rate_limit.rejected`** — every 429, from any named policy or the global ceiling
  (`RateLimitingServiceExtensions`'s shared `OnRejected` handler). One row per rejection, including
  under a sustained flood — a deliberate simplicity-over-throttling tradeoff; this table is not
  itself rate-limited or deduplicated. `ActorUserId` is always `null` here, since rate limiting runs
  before authentication in the pipeline (deliberately, so the ceiling also protects the auth
  surface itself); `ActorIp` and the triggering policy name (in `Metadata`) are recorded instead.
- **`auth.account.locked_out`** — written the moment ASP.NET Core Identity's own lockout counter
  trips, from the same code path that already owned that check.
- **`auth.email_throttle.blocked`** — written when `MemberService`/`AuthController`'s email-send
  throttle (verification/reset emails) blocks a resend.
- **`membership.approved`** — written by `MemberService.ApproveAsync` alongside the existing
  receipt/email side effects, once per application (idempotent, matching `ApproveAsync` itself —
  see `members.md`'s "RMP verification gates approval").
- **Not audited (out of scope for this iteration)**: role/permission changes (`roles.md` already
  flags this as an open TODO there — unresolved, not superseded by this feature), member
  profile/status edits (`members.md` flags the same), login successes/failures short of a lockout,
  content/layout CRUD. This table only covers the four event types above; it is not a general
  activity log for the whole application.
- All four event types share the `auth.*` or `membership.*` `EventType` prefix convention
  deliberately — `LogRetentionService` uses the `auth.` prefix specifically to select which rows are
  subject to the 90-day cutoff (see Retention below), so a future event type must pick its prefix
  with that in mind.

## Retention

Both tables are pruned by `LogRetentionBackgroundService`, a single daily `PeriodicTimer` job (the
first scheduled job in this codebase — a plain timer rather than a scheduling library, since there
is exactly one job at exactly one interval). It runs once immediately on startup, then once every
24h, so a restart-heavy deployment doesn't wait a full day for its first prune.

- **`AuditLog`**: rows whose `EventType` starts with `"auth."` are deleted once older than 90 days.
  `"membership.approved"` rows are *not* matched by that prefix and are therefore kept forever —
  membership approval is a durable business record (parallel to `PrcVerificationHistory`), not a
  security event with a natural expiry.
- **`ErrorLog`**: every row, `Backend` or `Frontend`, is deleted once older than 30 days. No
  exemptions — unlike `AuditLog`, nothing in this table is a record worth keeping indefinitely.

## Recording paths

- **Backend exceptions**: `ExceptionHandlingMiddleware` records every unhandled exception it
  catches (`ErrorSource.Backend`) before returning its existing standard error response — this
  feature only adds the recording, it doesn't change what gets returned to the caller.
- **Frontend exceptions**: two independent capture points, both funneling through the same
  `reportError` helper (`core/errorReporting/reportError.ts`) which calls `POST /api/errors/frontend`:
  - `AppErrorBoundary` (`core/errorReporting/AppErrorBoundary.tsx`), a class-component React error
    boundary wrapping the app, catches render-time errors via `componentDidCatch` and shows a
    generic "Something went wrong" / Reload fallback in place of a blank white screen.
  - `setupGlobalErrorHandlers` (`core/errorReporting/setupGlobalErrorHandlers.ts`), called once at
    bootstrap in `App.tsx`, adds `window` listeners for `error` and `unhandledrejection` — the two
    categories a React error boundary cannot catch on its own (event handlers, timers, promise
    rejections outside render).

## Frontend

`SystemLogsPage` (`/admin/system-logs`) has two tabs, Audit and Errors, each with its own search
box, filter (event type / source), date-range pickers, pagination, and a details modal for the raw
`Metadata`/`StackTrace` on a given row — mirroring the search-and-filter convention already
established on the Members and Users list pages (see `members.md`, and the search/role-filter work
on `/admin/users`).

## Open questions / TODO

- No dashboard or alerting on new errors — this is a queryable table, not a monitoring system. A
  spike in `ErrorLog` rows produces no notification; a Super Admin has to think to look.
- No de-duplication of repeated identical errors. The same frontend exception firing on every
  page load of a broken route writes one row per occurrence, not one row with a count — the Errors
  tab can be noisy under a sustained client-side bug.
- RMP/PRC verification decisions are **not** duplicated into `AuditLog`. That history remains
  solely in `PrcVerificationHistory` (see `members.md`), which predates this feature and already
  serves that purpose for its one specific workflow; `AuditLog` was not retrofitted to also cover it.
- Role/permission changes and member profile/status edits remain unaudited, per "What gets
  audited, and what doesn't" above — pre-existing gaps this feature didn't attempt to close.
