# membership-dashboard-stats Specification (Delta)

## ADDED Requirements

### Requirement: Membership Statistics Aggregation Endpoint

The system SHALL expose `GET /api/members/stats`, gated by the `members:view` permission (the
same permission that gates `GET /api/members`), returning a `MemberStatsDto` computed over the
same base set of members `GET /api/members` uses: submitted (non-draft) members, with system/
staff accounts excluded. The endpoint SHALL accept no query parameters — the response always
covers the full current dataset.

#### Scenario: A caller with Members.View fetches stats

- **WHEN** an authenticated caller holding the `members:view` permission calls
  `GET /api/members/stats`
- **THEN** the response is `200 OK` with a `MemberStatsDto` body

#### Scenario: A caller without Members.View is rejected

- **WHEN** an authenticated caller lacking `members:view` (e.g. a plain Member role) calls
  `GET /api/members/stats`
- **THEN** the response is `403 Forbidden`

#### Scenario: An unauthenticated caller is rejected

- **WHEN** an unauthenticated request calls `GET /api/members/stats`
- **THEN** the response is `401 Unauthorized`

### Requirement: Status Counts Are Always Fully Present

The response's `StatusCounts` SHALL report a count for all four `MembershipStatus` values
(Pending, Active, Expired, Deactivated), defaulting to zero for any status with no matching
members — never omitting a status merely because it currently has zero members.

#### Scenario: A status with zero members still appears as zero

- **WHEN** no member currently has `Status: Deactivated`
- **THEN** `StatusCounts.Deactivated` is `0` in the response, not omitted from the response shape

### Requirement: Registration Trend Covers the Last 12 Calendar Months, Zero-Filled

`RegistrationTrend` SHALL contain exactly 12 entries, one per calendar month from 11 months ago
through the current month (oldest first), each reporting the count of members whose
`SubmittedAt` falls in that month. A month with no submissions SHALL still appear with count `0`.

#### Scenario: A month with no submissions is zero-filled, not skipped

- **WHEN** no member submitted an application in a given month within the trailing 12-month
  window
- **THEN** that month still appears in `RegistrationTrend` with `Count: 0`, at its correct
  chronological position

### Requirement: Chapter and Member-Type Breakdowns Cover the Full Declared Lists, Zero-Filled

`ByChapter` SHALL contain one entry per value in the `Chapters.All` constant list, in that list's
declared order; `ByMemberType` SHALL contain one entry per value in `MemberTypes.All`, likewise in
declared order. A chapter or member type with no matching members SHALL still appear with count
`0`, rather than being omitted.

#### Scenario: A chapter with no current members still appears

- **WHEN** a chapter in `Chapters.All` (e.g. "Baguio") currently has no members assigned to it
- **THEN** `ByChapter` still contains an entry for "Baguio" with `Count: 0`

### Requirement: Action Items Report Pending Approvals, Pending PRC Verification, and Renewals Due Soon

`ActionItems.PendingApprovals` SHALL count members with `ApprovedAt == null` (within the base
set). `ActionItems.PendingPrcVerification` SHALL use the identical predicate the
`pendingPrcVerificationOnly` filter on `GET /api/members` already uses — the two SHALL NOT diverge
independently; any future change to what counts as "pending PRC verification" SHALL update both
call sites from a single shared definition. `ActionItems.RenewalsDueSoon` SHALL count members
with `Status: Active` whose `RenewalDueDate` falls within 60 days (inclusive of both the current
date and the 60-day boundary) of the request time.

#### Scenario: A renewal due in exactly 60 days counts as due soon

- **WHEN** an Active member's `RenewalDueDate` is exactly 60 days from now
- **THEN** they are included in `ActionItems.RenewalsDueSoon`

#### Scenario: A renewal due today counts as due soon

- **WHEN** an Active member's `RenewalDueDate` is today
- **THEN** they are included in `ActionItems.RenewalsDueSoon`

#### Scenario: An overdue renewal does not count as due soon

- **WHEN** an Active member's `RenewalDueDate` was yesterday
- **THEN** they are NOT included in `ActionItems.RenewalsDueSoon`

#### Scenario: Pending PRC verification stays consistent with the Members list filter

- **WHEN** a member matches `GET /api/members?pendingPrcVerificationOnly=true`
- **THEN** that same member is counted in `ActionItems.PendingPrcVerification`

### Requirement: Statistics Section Is Visible to Admin/Staff Only

The Dashboard's Statistics section (status breakdown, registration trend, chapter/member-type
breakdown, action items) SHALL be rendered only for non-Member roles (Admin, Super Admin,
Manager, Accounts, or any role without the Member role). It SHALL NOT be rendered for a user
holding the Member role.

#### Scenario: An Admin sees the Statistics section

- **WHEN** a user with an Admin/staff role (not Member) views the Dashboard
- **THEN** the Statistics section renders, showing real membership counts

#### Scenario: A Member does not see the Statistics section

- **WHEN** a user with the Member role views the Dashboard
- **THEN** the Statistics section does not render at all
