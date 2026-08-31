# error-log Specification (Delta)

## ADDED Requirements

### Requirement: Dedicated Error Log, Separate From Audit Log

The system SHALL maintain a dedicated `ErrorLog` table for unhandled exceptions and frontend
runtime errors, separate from `AuditLog`. Each row SHALL record a source (`Backend` or
`Frontend`), an exception type, a message, an optional stack trace, an optional request
path/method or frontend URL, an optional authenticated user id, an optional user agent, a
timestamp, and optional structured metadata. `Message` and `StackTrace` SHALL be length-capped at
write time.

#### Scenario: An error is recorded with its source

- **WHEN** a backend unhandled exception or a frontend runtime error occurs
- **THEN** exactly one `ErrorLog` row is created with `Source` set to `Backend` or `Frontend`
  respectively

### Requirement: Backend Unhandled Exceptions Are Captured

Every exception caught by `ExceptionHandlingMiddleware` SHALL be written to `ErrorLog` in addition
to the existing console log, recording the exception type, message, stack trace, and the request
path/method. This write SHALL be best-effort: a failure to record the error SHALL NOT change the
error response returned to the caller.

#### Scenario: An unhandled backend exception is logged and still returns 500

- **WHEN** a request causes an unhandled exception
- **THEN** the caller receives the existing HTTP 500 `ProblemDetails` response, unchanged
- **AND** an `ErrorLog` row of source `Backend` is written with the exception's type, message,
  stack trace, and the request path/method

#### Scenario: Error-log write failure does not change the error response

- **WHEN** an unhandled exception occurs and the `ErrorLog` write itself fails
- **THEN** the caller still receives the standard 500 response
- **AND** no additional exception surfaces to the caller

### Requirement: Frontend Errors Are Captured

The frontend SHALL capture three classes of client-side error and report each to a new
`POST /api/errors/frontend` endpoint: React render errors (via an error boundary wrapping the app
root), uncaught runtime errors (`window.onerror`), and unhandled promise rejections
(`window.onunhandledrejection`). Each report SHALL include the error message, stack trace (when
available), the current URL, and the browser's user agent. The endpoint SHALL accept
unauthenticated requests, since a frontend error can occur before login, but SHALL still record
the authenticated user id when a valid session is present.

#### Scenario: A React render crash is captured instead of a blank screen

- **WHEN** a component throws during render
- **THEN** the error boundary catches it and displays a fallback UI instead of a blank screen
- **AND** an `ErrorLog` row of source `Frontend` is written with the error and component stack

#### Scenario: An uncaught runtime error is captured

- **WHEN** an uncaught JavaScript error occurs outside React's render cycle (e.g. in an event
  handler)
- **THEN** the `window.onerror` handler reports it to `POST /api/errors/frontend`
- **AND** an `ErrorLog` row of source `Frontend` is written

#### Scenario: An unhandled promise rejection is captured

- **WHEN** a promise rejects with no `.catch` handler
- **THEN** the `unhandledrejection` handler reports it to `POST /api/errors/frontend`
- **AND** an `ErrorLog` row of source `Frontend` is written

#### Scenario: A frontend error before login still records without a user id

- **WHEN** a frontend error occurs while no user is authenticated (e.g. on the login page)
- **THEN** `POST /api/errors/frontend` still accepts the report
- **AND** the resulting `ErrorLog` row has a null `UserId`

### Requirement: The Frontend Error Endpoint Is Rate-Limited and Payload-Capped

Because `POST /api/errors/frontend` is unauthenticated and accepts free-text payloads, it SHALL be
protected by a dedicated named rate-limit policy (following the same mechanism as the auth
surface's `auth-ip`/`auth-email-send`/`username-probe` policies) and SHALL reject or truncate
`Message`/`StackTrace` payloads beyond a fixed length.

#### Scenario: Excessive frontend error reports from one IP are throttled

- **WHEN** a single client IP submits more frontend error reports than the configured policy
  allows within its window
- **THEN** further reports from that IP within the window are rejected with HTTP 429

#### Scenario: An oversized stack trace is capped, not rejected

- **WHEN** a frontend error report includes a stack trace longer than the configured maximum
- **THEN** the report is still accepted
- **AND** the stored `StackTrace` is truncated to the configured maximum length

### Requirement: Error Log Entries Are Pruned After 30 Days

A daily background job SHALL delete all `ErrorLog` rows whose `CreatedAt` is more than 30 days in
the past, regardless of source.

#### Scenario: An old error is pruned

- **WHEN** the daily pruning job runs
- **THEN** `ErrorLog` rows older than 30 days are deleted, from both `Backend` and `Frontend`
  sources

### Requirement: Error Log Viewable by Super Admin Only

A Super-Admin-only page SHALL display `ErrorLog` entries in a paginated, searchable, filterable,
read-only table, accessible from a dedicated "Errors" tab on the System Logs page (the same page
hosting the Audit tab). The table SHALL support free-text search, filtering by source, filtering
by date range, and a "View Details" action that displays the row's full stack trace. No edit or
delete action SHALL be exposed in this UI.

#### Scenario: A non-Super-Admin cannot reach the error log

- **WHEN** a user without the Super Admin role navigates to the Errors tab or calls
  `GET /api/admin/error-log` directly
- **THEN** the request is rejected (route blocked client-side; 403 server-side)

#### Scenario: A Super Admin searches and filters the error log

- **WHEN** a Super Admin opens the Errors tab and enters a search term, selects a source filter, or
  sets a date range
- **THEN** the table narrows to matching rows, server-side, resetting to page 1 on each change

#### Scenario: Viewing details of an error row

- **WHEN** a Super Admin clicks "View Details" on an error row
- **THEN** the row's full stack trace is displayed in a modal
