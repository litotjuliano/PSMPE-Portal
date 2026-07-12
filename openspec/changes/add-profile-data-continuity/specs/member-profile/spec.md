# member-profile Specification (Delta)

## ADDED Requirements

### Requirement: Single Source of Truth for Member Data

The system SHALL persist all data captured in the 4-step membership registration wizard on the canonical Member record. Approval SHALL only transition the record's status and SHALL NOT create, copy, or duplicate data into a separate profile dataset.

#### Scenario: Approval transitions status without data duplication

- **WHEN** an administrator approves a pending membership application
- **THEN** the member's status changes from Pending to Active
- **AND** no separate profile record is created
- **AND** all fields submitted during registration remain on the same member record

### Requirement: Profile Auto-Population After Submission

The My Profile page SHALL display every field submitted during registration steps 1–4 in its corresponding section, immediately after submission, without requiring the member to re-enter any data. This applies once the application is submitted (`SubmittedAt` set) — the same point the wizard already stops applying — not gated on admin approval.

#### Scenario: All registration fields visible after submission

- **WHEN** a member who has submitted their application opens My Profile
- **THEN** First Name, Last Name, Middle Name, Suffix, Birthdate, Gender, Address, PRC License No., Chapter, Company, and Member Type display the values submitted during registration
- **AND** the header shows Membership No., Status, Email, and Member Type

#### Scenario: Uploaded files carried over

- **WHEN** a member who has submitted their application opens My Profile
- **THEN** the profile photo uploaded during registration is displayed
- **AND** the PRC ID document uploaded during registration is viewable/retrievable

#### Scenario: Registration collects every field the profile shows

- **WHEN** a member completes the registration wizard
- **THEN** step 1 (Personal Information) collects PRC License No. alongside the PRC ID document upload
- **AND** step 4 (Additional Information) collects Company
- **AND** every field the post-submit profile displays was collected somewhere in the wizard

### Requirement: Post-Submit Tabbed Profile

Once an application is submitted, the wizard SHALL no longer apply. My Profile SHALL instead present the same data grouped into 4 tabs/sections mirroring the wizard's original steps: Personal Information, Contact Information, Account Information, Additional Information. Each tab SHALL have its own independent View Mode (default, read-only) and Edit Mode with its own Save action — a member edits one section at a time.

#### Scenario: Default read-only display per tab

- **WHEN** a member who has submitted their application navigates to My Profile
- **THEN** each tab renders its fields as read-only values by default
- **AND** each tab has its own "Edit" action to switch that tab into Edit Mode

#### Scenario: Editing one section does not affect others

- **WHEN** a member switches Personal Information into Edit Mode
- **THEN** Contact Information, Account Information, and Additional Information remain in View Mode, unaffected

#### Scenario: Member edits and saves a section's self-service fields

- **WHEN** a member switches Contact Information's Edit Mode, changes Address, and clicks Save
- **THEN** the change is validated and persisted to the member record
- **AND** that tab's View Mode displays the updated value
- **AND** the admin member-management view reflects the same value

#### Scenario: Validation failure blocks save

- **WHEN** a member submits a section's Edit Mode with an invalid value (e.g., malformed birthdate)
- **THEN** the save is rejected with a validation message
- **AND** no partial changes are persisted

#### Scenario: Account Information has nothing to edit

- **WHEN** a member views the Account Information tab
- **THEN** Email and Display Name render read-only
- **AND** no Edit action is offered on this tab, since Email is out of scope for editing (see the PRC/Email requirement below) and nothing else on this tab is self-service

### Requirement: Protected System-Controlled Fields

Membership No., Status, and Email SHALL always be read-only in Edit Mode. Member Type and Chapter SHALL be freely editable while the application is a draft (`SubmittedAt == null`, mid-wizard), but the instant the application is submitted, both SHALL lock permanently and SHALL only change through an admin-managed process (`PUT /api/members/{id}`) thereafter — never through the member's own Edit Mode again, including via a crafted request. Approval SHALL NOT further change this lock state; the lock is tied to submission, not approval.

#### Scenario: System fields are never editable

- **WHEN** a member is in Edit Mode, at any point before or after submission
- **THEN** Membership No., Status, and Email render as read-only
- **AND** any attempt to modify them via a crafted request is rejected server-side

#### Scenario: Member Type and Chapter are editable during draft

- **WHEN** a member is completing the registration wizard and the application has not yet been submitted
- **THEN** Member Type and Chapter can be freely changed as part of completing Personal Information

#### Scenario: Member Type and Chapter lock at submission

- **WHEN** a member's application has been submitted (`SubmittedAt` set) and the member attempts to change Member Type or Chapter, whether via My Profile or a direct/crafted request
- **THEN** the server rejects the change
- **AND** the member is informed these fields are now managed by administration
- **AND** this remains true after admin approval as well — approval does not reopen or further restrict the fields beyond the submission-time lock

### Requirement: PRC License No. Edits Require Re-Verification, Staged as a Pending Value

Editing **PRC License No.** SHALL require re-uploading the PRC ID document in the same edit. The proposed new value SHALL be staged as `PendingPrcLicenseNo` rather than overwriting `PrcLicenseNo` immediately — the prior value remains current (shown everywhere: View Mode, the admin member record) until an administrator decides via the PRC Verification Queue (see the requirement below). Email changes SHALL NOT be supported by this change — email remains read-only in every context, and no email-change flow is offered.

#### Scenario: Changing PRC License No. without a new PRC ID is rejected

- **WHEN** a member submits Edit Mode with a changed PRC License No. but no accompanying PRC ID re-upload
- **THEN** the save is rejected with a validation message requiring the PRC ID document
- **AND** no partial changes are persisted

#### Scenario: Changing PRC License No. with a new PRC ID stages a pending value

- **WHEN** a member submits Edit Mode with a changed PRC License No. and a re-uploaded PRC ID document
- **THEN** the change is accepted, but `PrcLicenseNo` itself is NOT changed — the new value is stored as `PendingPrcLicenseNo`
- **AND** the member becomes visible to admins in the PRC Verification Queue
- **AND** any earlier `PrcVerificationRejectedReason` is cleared, since this new attempt supersedes it

#### Scenario: Saving without changing PRC License No. does not affect verification

- **WHEN** a member saves Edit Mode changes that do not touch PRC License No.
- **THEN** `PrcIdVerified` and `PendingPrcLicenseNo` are left unchanged

#### Scenario: Email is never editable

- **WHEN** a member is in Edit Mode, or submits a crafted request attempting to change Email
- **THEN** the change is rejected server-side
- **AND** no email-change flow is offered anywhere in the product

### Requirement: Admin PRC Verification Queue (Approve/Reject/Audit)

A dedicated admin queue (mirrors the existing Membership Approvals queue/topbar notification pattern) SHALL list every member with a pending PRC License No. change (`PendingPrcLicenseNo` set) **or** a PRC License No. that has never been reviewed at all (`PrcIdVerified` false, nothing pending — covers a member's first-time-submitted PRC License No., which otherwise has no admin-facing surface). From this queue, an administrator SHALL be able to **Approve** (the pending value, if any, becomes the current `PrcLicenseNo`; `PrcIdVerified` is set `true`) or **Reject** (the pending value is discarded; a required reason is recorded and shown to the member). Every decision SHALL be recorded in an audit trail (old value, new value, a reference to the PRC ID document, the decision, the reason if rejected, the deciding admin, and when). The raw, ungated "PRC ID Verified" toggle on the generic admin Member edit form is REMOVED — `PrcIdVerified` SHALL only ever change via Approve/Reject, never a direct edit.

#### Scenario: Never-reviewed PRC License No. appears in the queue

- **WHEN** a member submits their application with a PRC License No. that has never been reviewed
- **THEN** they appear in the PRC Verification Queue even though nothing is pending

#### Scenario: Approving a pending change commits it

- **WHEN** an administrator Approves a member with a pending PRC License No. change
- **THEN** `PrcLicenseNo` is updated to the pending value, `PendingPrcLicenseNo` is cleared, and `PrcIdVerified` becomes `true`
- **AND** an Approved entry is recorded in the audit trail

#### Scenario: Approving a never-reviewed PRC License No. just marks it verified

- **WHEN** an administrator Approves a member who has no pending change but has never been verified
- **THEN** `PrcLicenseNo` is unchanged and `PrcIdVerified` becomes `true`

#### Scenario: Rejecting a pending change discards it and leaves the current value standing

- **WHEN** an administrator Rejects a member's pending PRC License No. change with a reason
- **THEN** `PendingPrcLicenseNo` is cleared and `PrcLicenseNo` is unchanged
- **AND** the member sees the rejection reason on their Personal Information tab
- **AND** a Rejected entry (with the reason) is recorded in the audit trail

#### Scenario: Rejecting a never-reviewed member keeps them in the queue

- **WHEN** an administrator Rejects a member who had no pending change (a first-time review)
- **THEN** the member remains in the PRC Verification Queue (still unverified) with the reason attached, until they submit a new attempt or an admin later approves

#### Scenario: No bypass path exists

- **WHEN** an administrator edits a member via the generic `PUT /api/members/{id}` form
- **THEN** there is no field there to directly set `PrcIdVerified` — only Approve/Reject can change it, ensuring every decision is captured in the audit trail

## REMOVED Requirements

### Requirement: Backfill of Existing Approved Members

**Reason**: no legacy "application" dataset separate from the Member profile has ever existed in this codebase — registration and profile have always been the same row (see "Single Source of Truth for Member Data" above). A backfill would be a no-op by construction, since there is nothing to backfill *from*. Cut rather than implemented; see `proposal.md`'s Impact section and `tasks.md`.

The system was originally proposed to backfill profile fields for members approved before this change, using their original application data where profile fields were empty — this assumed a split that does not exist here.
