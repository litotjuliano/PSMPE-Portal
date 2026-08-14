# audit-log Specification (Delta)

## ADDED Requirements

### Requirement: Generic Append-Only Audit Log

The system SHALL maintain a single, generic `AuditLog` table capturing security and business
decision events across domains, rather than a dedicated history table per domain. Each row SHALL
record an event type, an optional actor (user id and/or IP), an optional target (type and id), a
timestamp, and optional structured metadata. Rows SHALL never be updated or deleted except by the
retention pruning job.

#### Scenario: An audited event is recorded

- **WHEN** any of the four audited event types below occurs
- **THEN** exactly one `AuditLog` row is created with that event's type, actor, target, and
  metadata
- **AND** no existing `AuditLog` row is modified

### Requirement: Rate Limiter Rejections Are Audited

Every request rejected by a named rate-limit policy (`auth-ip`, `auth-email-send`,
`username-probe`) or the global ceiling SHALL write an `AuditLog` row of type
`auth.rate_limit.rejected`, recording which policy rejected the request (or `"global"`) and the
resolved client IP as `ActorIp`. `ActorUserId` SHALL always be null for this event type: rate
limiting (`app.UseRateLimiter()`) runs before authentication (`app.UseAuthentication()`) in the
request pipeline, deliberately, so that the global ceiling protects the authentication surface
itself rather than only requests that already passed it — which means no request has an
authenticated identity available yet at the point a rejection occurs, regardless of which policy
rejected it. This write SHALL be best-effort: a failure to record the event SHALL NOT prevent the
429 response from being returned.

#### Scenario: An IP exceeding the login rate limit is audited

- **WHEN** a client exceeds the `auth-ip` policy's limit on `POST /auth/login`
- **THEN** the request is rejected with HTTP 429
- **AND** an `AuditLog` row of type `auth.rate_limit.rejected` is written with the client's IP and
  the policy name `auth-ip`

#### Scenario: An already-logged-in caller hitting the global ceiling is still audited without an identity

- **WHEN** an already-logged-in user's requests are rejected by the global rate-limit ceiling
- **THEN** an `AuditLog` row of type `auth.rate_limit.rejected` is written with `ActorIp` set and
  `ActorUserId` null, and the policy name `"global"`

#### Scenario: Audit write failure does not break the 429 response

- **WHEN** a rate limit is exceeded and the `AuditLog` write fails (e.g. the database is
  unreachable)
- **THEN** the caller still receives HTTP 429 with the standard `Retry-After` header
- **AND** no exception surfaces to the caller

### Requirement: Account Lockouts Are Audited

The moment a failed login attempt transitions an account into a locked-out state (Identity's
`MaxFailedAccessAttempts` threshold), the system SHALL write an `AuditLog` row of type
`auth.account.locked_out`, recording the affected user as `ActorUserId`. A login rejected because
the account was *already* locked SHALL NOT write a second row.

#### Scenario: The attempt that trips the lockout threshold is audited

- **WHEN** a failed login attempt causes an account's failed-attempt count to reach the configured
  maximum
- **THEN** the account becomes locked out
- **AND** an `AuditLog` row of type `auth.account.locked_out` is written for that user

#### Scenario: Logging in against an already-locked account does not duplicate the event

- **WHEN** a login is attempted against an account that was already locked out before this attempt
- **THEN** the request is rejected with HTTP 403
- **AND** no new `AuditLog` row is written

### Requirement: Email-Throttle Blocks Are Audited

Every time `MemoryCacheEmailSendThrottle` blocks a forgot-password or resend-verification email to
a given address, the system SHALL write an `AuditLog` row of type `auth.email_throttle.blocked`.

#### Scenario: A throttled email send is audited

- **WHEN** a caller requests a password-reset email for an address that has already reached its
  per-address send limit within the current window
- **THEN** no email is sent
- **AND** an `AuditLog` row of type `auth.email_throttle.blocked` is written

### Requirement: Membership Approval Is Audited

Every successful membership application approval (`MemberService.ApproveAsync`) SHALL write an
`AuditLog` row of type `membership.approved`, recording the approving admin as `ActorUserId` and
the approved member as the target (`TargetType = "Member"`, `TargetId` = the member's id). This
write SHALL occur within the same database transaction as the approval itself, so the two either
both succeed or both fail together.

#### Scenario: Approving an application writes an audit row atomically

- **WHEN** an administrator approves a pending membership application
- **THEN** the member's `ApprovedAt` is set
- **AND** in the same transaction, an `AuditLog` row of type `membership.approved` is written with
  the approving admin as actor and the member as target

#### Scenario: A failed approval writes no audit row

- **WHEN** an approval attempt fails validation (e.g. a missing Membership No., or an unverified
  RMP license) and no changes are persisted
- **THEN** no `AuditLog` row is written

#### Scenario: Re-approving an already-approved application is a no-op

- **WHEN** approval is called on a member that is already approved
- **THEN** the call succeeds without modifying `ApprovedAt`
- **AND** no additional `AuditLog` row is written

### Requirement: Security Events Are Pruned After 90 Days; Approval Events Are Kept Indefinitely

A daily background job SHALL delete `AuditLog` rows whose `EventType` begins with `auth.` and
whose `CreatedAt` is more than 90 days in the past. Rows of type `membership.approved` SHALL never
be pruned by this job.

#### Scenario: An old rate-limit rejection is pruned

- **WHEN** the daily pruning job runs
- **THEN** `auth.rate_limit.rejected`, `auth.account.locked_out`, and
  `auth.email_throttle.blocked` rows older than 90 days are deleted

#### Scenario: An old approval event is not pruned

- **WHEN** the daily pruning job runs
- **THEN** `membership.approved` rows are left untouched regardless of age

### Requirement: Audit Log Viewable by Super Admin Only

A Super-Admin-only page SHALL display `AuditLog` entries in a paginated, searchable, filterable,
read-only table, accessible from a dedicated "Audit" tab on the System Logs page. The table SHALL
support free-text search, filtering by event type, filtering by date range, and a "View Details"
action that displays the row's full `Metadata`. No edit or delete action SHALL be exposed in this
UI.

#### Scenario: A non-Super-Admin cannot reach the audit log

- **WHEN** a user without the Super Admin role navigates to `/admin/system-logs` or calls
  `GET /api/admin/audit-log` directly
- **THEN** the request is rejected (route blocked client-side; 403 server-side)

#### Scenario: A Super Admin searches and filters the audit log

- **WHEN** a Super Admin opens the Audit tab and enters a search term, selects an event type
  filter, or sets a date range
- **THEN** the table narrows to matching rows, server-side, resetting to page 1 on each change

#### Scenario: Viewing details of an audit row

- **WHEN** a Super Admin clicks "View Details" on an audit row
- **THEN** the row's full `Metadata` is displayed in a modal
