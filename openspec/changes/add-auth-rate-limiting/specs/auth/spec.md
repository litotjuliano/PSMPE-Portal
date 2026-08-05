# auth Specification (Delta)

## ADDED Requirements

### Requirement: Client IP Resolution Behind the Reverse Proxy

The system SHALL resolve the originating client IP address from the `X-Forwarded-For` header set
by the reverse proxy, and SHALL trust that header only when the immediate peer is a known proxy.
The system SHALL consume exactly one forwarded entry (`ForwardLimit = 1`), so that a
client-supplied `X-Forwarded-For` value cannot influence the resolved address.

#### Scenario: Client IP resolved from a trusted proxy

- **WHEN** a request arrives from the Docker bridge gateway with `X-Forwarded-For: 203.0.113.7`
- **THEN** the resolved client IP is `203.0.113.7`
- **AND** rate limit partitions are keyed on `203.0.113.7`

#### Scenario: Forged forwarded header from an untrusted peer is ignored

- **WHEN** a request arrives from an address outside the known-proxy networks carrying
  `X-Forwarded-For: 198.51.100.1`
- **THEN** the header is not honoured
- **AND** the resolved client IP is the actual peer address

#### Scenario: Appended forwarded chain uses only the proxy-supplied entry

- **WHEN** a client sends `X-Forwarded-For: 198.51.100.1` and the proxy appends the real peer,
  producing `198.51.100.1, 203.0.113.7`
- **THEN** the resolved client IP is `203.0.113.7`
- **AND** the client-supplied `198.51.100.1` is disregarded

#### Scenario: Misconfigured trust chain is reported

- **WHEN** a resolved client IP falls inside the known-proxy range, indicating forwarded headers
  are not arriving
- **THEN** the system logs a warning once per process

### Requirement: Rate Limiting of Public Authentication Endpoints

The system SHALL apply fixed-window rate limits, partitioned by resolved client IP, to the public
authentication endpoints. Limits SHALL be configurable, and SHALL be disableable in full via a
`RateLimit:Enabled` kill switch.

| Policy | Endpoints | Limit |
|---|---|---|
| `auth-ip` | `login`, `register`, `verify-email`, `reset-password` | 20 / 5 min |
| `auth-email-send` | `forgot-password`, `resend-verification-email` | 10 / hour |
| `username-probe` | `username-available` | 30 / min |
| `global` | all other endpoints | 300 / min |

#### Scenario: Requests within the limit are served

- **WHEN** a client makes 20 `login` requests within 5 minutes
- **THEN** every request is processed normally

#### Scenario: Requests beyond the limit are rejected

- **WHEN** a client makes a 21st `login` request within the same 5 minute window
- **THEN** the response status is 429
- **AND** the body is `ProblemDetails` with content type `application/problem+json`
- **AND** a `Retry-After` header is present

#### Scenario: Limits partition by client IP

- **WHEN** one client has exhausted its `auth-ip` allowance
- **THEN** a request from a different client IP is still served normally

#### Scenario: Username availability tolerates typeahead usage

- **WHEN** a member fills in the registration form, producing 15 debounced
  `username-available` calls within a minute
- **THEN** every call is served normally

#### Scenario: Rate limiting can be disabled

- **WHEN** `RateLimit:Enabled` is `false`
- **THEN** no request is rejected with 429 regardless of volume

### Requirement: Account Lockout on Repeated Failed Logins

The system SHALL count consecutive failed password attempts per account and SHALL lock the account
after 5 failures for 15 minutes. A successful login SHALL reset the count. Lockout SHALL be
enforced independently of client IP, so that an attacker rotating IP addresses gains nothing.

#### Scenario: Account locks after repeated failures

- **WHEN** 5 consecutive login attempts for one account use an incorrect password
- **AND** a 6th attempt is made
- **THEN** the response status is 403
- **AND** the body carries `code = "ACCOUNT_LOCKED"`

#### Scenario: Lockout holds across differing client IPs

- **WHEN** the 5 failed attempts originate from 5 different client IP addresses
- **THEN** the account is still locked on the 6th attempt

#### Scenario: Successful login resets the failure count

- **WHEN** an account has 4 failed attempts recorded
- **AND** the next attempt supplies the correct password
- **THEN** login succeeds
- **AND** the recorded failure count returns to zero

#### Scenario: Lockout does not leak account existence

- **WHEN** repeated login attempts are made against an email address with no account
- **THEN** the response remains the existing generic invalid-credentials response

### Requirement: Per-Address Throttling of Outbound Account Emails

The system SHALL limit `forgot-password` and `resend-verification-email` to 3 sends per email
address per hour, partitioned by the submitted address rather than by client IP. A throttled
request SHALL NOT reveal whether the address corresponds to an existing account.

#### Scenario: Fourth send to one address within the hour is throttled

- **WHEN** 3 `forgot-password` requests have been made for one address within an hour
- **AND** a 4th request is made for the same address
- **THEN** no email is sent

#### Scenario: Throttling is per address, not global

- **WHEN** one address has exhausted its hourly allowance
- **THEN** a request for a different address still sends normally

#### Scenario: Throttled response does not enumerate accounts

- **WHEN** a `forgot-password` request is throttled
- **THEN** the response is the same generic response returned for an unthrottled request

### Requirement: Client Handling of Throttled Responses

The web client SHALL surface a 429 response as a retry-after message and SHALL NOT treat it as a
session termination.

#### Scenario: Throttled request keeps the user signed in

- **WHEN** an authenticated user receives a 429 response
- **THEN** the stored session is retained
- **AND** the user is not redirected to `/login`
- **AND** a message indicating when to retry is shown

### Requirement: Client IP Diagnostics

The system SHALL expose an administrator-only endpoint returning the resolved client IP, so the
forwarded-header trust chain can be verified after deployment.

#### Scenario: Administrator verifies the resolved address

- **WHEN** an administrator requests `GET /api/admin/diagnostics/client-ip`
- **THEN** the response contains the client IP as resolved by the forwarded-headers middleware

#### Scenario: Non-administrators are refused

- **WHEN** a member without administrative permission requests the endpoint
- **THEN** the request is refused by the existing authorization rules
