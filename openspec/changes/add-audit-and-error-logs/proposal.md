# Change: Add Audit Log, Error Log, and a Super-Admin System Logs Page

## Status

**Proposed.** Designed via brainstorming; not yet built.

## Why

Two unrelated gaps surfaced during a review of the auth-rate-limiting feature
(`add-auth-rate-limiting`, implemented):

1. **No audit trail.** `MemberService.ApproveAsync` records only `ApprovedAt` on the member row —
   there is no record of *who* approved an application, and the rate limiter, account lockout, and
   email-send throttle all reject requests silently with no trace beyond a transient HTTP 429/403.
   The one precedent in the codebase, `PrcVerificationHistory`, covers RMP license decisions only.
2. **No error visibility.** `ExceptionHandlingMiddleware` already catches every unhandled backend
   exception, but only logs it to console — nothing persists it. The frontend (`apps/web`) has no
   error boundary, no `window.onerror`, no unhandled-rejection handler at all; a render crash today
   is a silent blank screen with zero record anywhere.

## What Changes

- **`AuditLog`** — a new, generic, append-only table. Four write points: rate-limiter 429
  rejections, Identity account lockouts, email-send throttle blocks, and membership application
  approval. Security-event rows (the first three) are pruned after 90 days; `membership.approved`
  rows are kept indefinitely.
- **`ErrorLog`** — a new, dedicated table for unhandled exceptions, separate from `AuditLog`
  because errors have a different shape (stack traces) and a different volume profile (a bad
  deploy can generate thousands of identical rows in minutes). Written from
  `ExceptionHandlingMiddleware` (backend) and a new `POST /api/errors/frontend` endpoint (frontend,
  via a new React error boundary plus `window.onerror`/`unhandledrejection` handlers). Pruned after
  30 days.
- **System Logs page** — a new Super-Admin-only page (`/admin/system-logs`) with an **Audit** tab
  and an **Errors** tab, each a paginated, searchable, filterable, read-only table, following the
  existing list-page conventions (`AdminUsersPage`, `MembersPage`) and the standing rule that every
  list ships with search and filter.

## Decisions

Each resolved by the user during brainstorming:

- **Generic `AuditLog` over per-domain history tables** (the `PrcVerificationHistory` pattern) —
  one reusable table/service that future domains can write to without a new migration each time,
  rather than repeating a dedicated table per feature.
- **`AuditLog` scope**: rate-limiter 429s, account lockouts, and email-throttle blocks (all three
  auth-rate-limiting rejection paths), plus membership application approval only. RMP
  verification decisions are explicitly **not** duplicated into `AuditLog` — they stay solely in
  `PrcVerificationHistory`.
- **429s get a DB row like everything else** — rejected the alternative of routing high-volume 429s
  to structured logs only, since the existing rate limits are loose enough (20/5min, 300/min
  global) that realistic volume doesn't threaten Postgres, and a single write path is simpler than
  two.
- **`AuditLog` writes are best-effort** for the three WebAPI-layer events (429, lockout,
  email-throttle) — a logging failure must never turn into a broken login or a 500 on a rejected
  request. The membership-approval event is the exception: it's added to the same
  `SaveChangesAsync` call `ApproveAsync` already makes, so it's naturally atomic with the approval
  itself, no special-casing needed.
- **`AuditLog` retention**: 90-day pruning for the three security event types via a new
  `BackgroundService` (the first scheduled job in this codebase — no `IHostedService` exists
  today). `membership.approved` rows are exempt — low volume, treated as a permanent business
  record.
- **`ErrorLog` is a separate table from `AuditLog`**, not a shared `EventType`, because stack
  traces are large/variable and error volume can spike hard during an incident in a way that would
  distort `AuditLog`'s size profile and complicate its "keep membership rows forever" rule.
- **Errors are self-hosted**, not sent to a third-party service (e.g. Sentry) — no new external
  dependency, no error/request data leaving the droplet, consistent with this project's fully
  self-hosted Docker Compose deployment.
- **`ErrorLog` retention**: 30 days — errors are a near-term debugging aid, not a compliance
  record; if one still matters after a month it should already be a fixed, tracked bug, not a row
  being dug up.
- **One System Logs page with two tabs**, not two separate nav entries — mirrors how
  `consolidate-member-admin-lists` already folded three nav entries into tabs on one page,
  specifically to avoid growing the nav for something only Super Admin ever opens.
- **Super Admin only** — stricter than the general `/admin/*` gate (`Admin`, `Super Admin`,
  `Approval`), matching how `RequireSuperAdmin` already gates the most sensitive operations in
  `AdminController` (role assignment, user deletion).

## Design

### `AuditLog` (Domain entity, extends `BaseEntity` for `Id`/`CreatedAt`)

`EventType` (string — `"auth.rate_limit.rejected"`, `"auth.account.locked_out"`,
`"auth.email_throttle.blocked"`, `"membership.approved"`), `ActorUserId` (`Guid?`, null for
unauthenticated events), `ActorIp` (`string?`), `TargetType`/`TargetId` (`string?`/`Guid?`, e.g.
`"Member"` + the member's id), `Metadata` (`string?`, JSON — which policy tripped, old/new
Membership No., etc).

`IAuditLogService.RecordAsync(eventType, actorUserId, actorIp, targetType, targetId,
metadataJson, cancellationToken)`, interface in `Application/Common/Interfaces` (mirrors
`IEmailSendThrottle`), implemented in `Infrastructure/Services` against `IApplicationDbContext`.

**Write points:**
- `RateLimitingServiceExtensions.OnRejected` resolves `IAuditLogService` from
  `context.HttpContext.RequestServices` (same pattern the file already uses for `ILoggerFactory`)
  and records which named policy rejected, or `"global"` for the blanket ceiling.
- `AuthController.Login`, at the point that re-checks `IsLockedOutAsync` immediately after
  `AccessFailedAsync` — the exact line that already distinguishes "this attempt tripped the
  threshold" from a plain wrong-password 401.
- Wherever `AuthController` handles a `false` return from `MemoryCacheEmailSendThrottle.TryRecordSend`.
- `MemberService.ApproveAsync`, added to the existing `db.SaveChangesAsync` call.

### `ErrorLog` (Domain entity, extends `BaseEntity`)

`Source` (enum: `Backend`/`Frontend`), `ExceptionType` (`string?`), `Message` (`string`, length-capped),
`StackTrace` (`string?`, `text` column, length-capped), `RequestPath`/`RequestMethod` (`string?`),
`Url` (`string?` — frontend route), `UserId` (`Guid?`), `UserAgent` (`string?`), `Metadata`
(`string?` JSON — e.g. React component stack).

`IErrorLogService.RecordAsync(...)`, same best-effort semantics as the `AuditLog` writes.

**Backend capture**: added to `ExceptionHandlingMiddleware`, alongside its existing
`logger.LogError` call.

**Frontend capture** (net new): a React error boundary wrapping the app root; a
`window.addEventListener('error', ...)` handler; a `window.addEventListener('unhandledrejection',
...)` handler. All three POST to `/api/errors/frontend`.

**`/api/errors/frontend` guardrails**: this endpoint is necessarily unauthenticated (errors can
happen before login) and accepts free-text payloads, so it gets its own named rate-limit policy in
`RateLimitingServiceExtensions` (same mechanism already protecting the auth surface) plus
server-side length caps on `Message`/`StackTrace`.

### Pruning

A new `BackgroundService` (Infrastructure layer), running once daily: deletes `AuditLog` rows
where `EventType` starts with `auth.` and `CreatedAt` is older than 90 days; deletes all `ErrorLog`
rows older than 30 days. `membership.approved` rows in `AuditLog` are never pruned.

### System Logs page (`/admin/system-logs`, Super Admin only)

New nav item `System Logs` in `menu.ts` with `requiredRoles: ['Super Admin']`; new route wrapped in
`ProtectedRoute requiredRoles={[Roles.SuperAdmin]}`.

**Audit tab** — columns: Timestamp, Event Type, Actor (email, or "—"), IP, Target, and a "View
Details" action opening `Metadata` in a modal (reusing the existing modal pattern from
`ApproveApplicationWizard`).

**Errors tab** — columns: Timestamp, Source badge, Exception Type, Message (truncated), User, Path/URL,
and the same "View Details" modal pattern for the full stack trace.

**Search & filter** on both tabs: server-side debounced free-text search, an Event Type filter
(Audit) / Source filter (Errors), and a date range filter — new for this page, since both tables
are inherently time-ordered in a way Members/Users aren't.

**Backend**: `GET /api/admin/audit-log` and `GET /api/admin/error-log`, both
`[Authorize(Policy = PolicyNames.RequireSuperAdmin)]`, paginated the same way as
`GET /api/admin/users`.

Both tabs are entirely read-only — no edit/delete UI. The pruning job is the only thing that ever
removes rows.

## Not Changed (this round)

- No dashboards, alerting, or notifications on new errors.
- No de-duplication/grouping of repeated identical errors — each occurrence is its own row.
- No third-party error-monitoring integration.
- RMP verification decisions are not duplicated into `AuditLog` — `PrcVerificationHistory` remains
  their sole record.
- No manual-deletion UI for either log — pruning is fully automatic.
