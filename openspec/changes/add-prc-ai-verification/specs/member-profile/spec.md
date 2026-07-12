# member-profile Specification (Delta)

## MODIFIED Requirements

### Requirement: PRC License No. Edits Require Re-Verification, Staged as a Pending Value

Editing **PRC License No., Registration Date, or Valid Until** SHALL require re-uploading the PRC
ID document in the same edit (changing any one of the three, since together they describe the
same physical PRC ID card - see `proposal.md` Open Decision 7). The proposed new values SHALL be
staged as `PendingPrcLicenseNo`/`PendingPrcRegistrationDate`/`PendingPrcValidUntilDate` rather than
overwriting the current values immediately - the prior values remain current (shown everywhere:
View Mode, the admin member record) until an administrator decides via the PRC Verification Queue
(see the requirement below), and are committed or discarded together as one unit. Additionally, an
administrator MAY set `Member.PrcIdReuploadRequested` (via the new Request Additional Documents
action - see the `prc-verification` capability) to require a fresh re-upload even when none of
these three fields has changed, for cases where the entered text was correct but the document
image itself needs to be redone. Email changes SHALL NOT be supported by this change - email
remains read-only in every context, and no email-change flow is offered.

#### Scenario: Changing any of the three PRC fields without a new PRC ID is rejected

- **WHEN** a member submits Edit Mode with a changed PRC License No., Registration Date, or Valid
  Until, but no accompanying PRC ID re-upload
- **THEN** the save is rejected with a validation message requiring the PRC ID document
- **AND** no partial changes are persisted

#### Scenario: Changing PRC fields with a new PRC ID stages pending values together

- **WHEN** a member submits Edit Mode with a changed PRC License No. and/or Registration Date
  and/or Valid Until, along with a re-uploaded PRC ID document
- **THEN** the changes are accepted, but the current values are NOT changed - the new values are
  stored as `PendingPrcLicenseNo`/`PendingPrcRegistrationDate`/`PendingPrcValidUntilDate`
- **AND** the member becomes visible to admins in the PRC Verification Queue
- **AND** any earlier `PrcVerificationRejectedReason` is cleared, since this new attempt supersedes
  it

#### Scenario: An admin-requested re-upload does not require the PRC fields to change

- **WHEN** an administrator has set `PrcIdReuploadRequested` for a member (via Request Additional
  Documents) and the member re-uploads a PRC ID document without changing any of the three PRC
  fields
- **THEN** the save is still accepted and triggers a fresh review, even though nothing about the
  gate's usual "did the text change" check would otherwise have tripped
- **AND** `PrcIdReuploadRequested` is cleared once the save succeeds

#### Scenario: Saving without changing any PRC field does not affect verification

- **WHEN** a member saves Edit Mode changes that do not touch PRC License No., Registration Date,
  or Valid Until, and `PrcIdReuploadRequested` is not set
- **THEN** `PrcIdVerified` and all pending PRC fields are left unchanged

#### Scenario: Email is never editable

- **WHEN** a member is in Edit Mode, or submits a crafted request attempting to change Email
- **THEN** the change is rejected server-side
- **AND** no email-change flow is offered anywhere in the product

### Requirement: Admin PRC Verification Queue (Approve/Reject/Audit)

A dedicated admin queue (mirrors the existing Membership Approvals queue/topbar notification
pattern) SHALL list every member with a pending PRC field change (any of
`PendingPrcLicenseNo`/`PendingPrcRegistrationDate`/`PendingPrcValidUntilDate` set) **or** a PRC
License No. that has never been reviewed at all (`PrcIdVerified` false, nothing pending - covers
a member's first-time-submitted PRC License No., which otherwise has no admin-facing surface).
Each member in the queue SHALL have a dedicated PRC Verification detail page (see the
`prc-verification` capability) where an administrator, optionally assisted by AI extraction, SHALL
be able to **Approve** (the pending values, if any, become current; `PrcIdVerified` is set
`true`), **Reject** (the pending values are discarded; a required reason is recorded and shown to
the member), or **Request Additional Documents** (sets `PrcIdReuploadRequested`; a required reason
is recorded and shown to the member). Every decision SHALL be recorded in an audit trail (old
values, new values, a reference to the PRC ID document, the decision, the reason if
rejected/documents-requested, the deciding admin, when, and optionally which AI extraction run, if
any, informed the decision). The raw, ungated "PRC ID Verified" toggle on the generic admin Member
edit form remains REMOVED - `PrcIdVerified` SHALL only ever change via Approve, never a direct
edit.

#### Scenario: Never-reviewed PRC License No. appears in the queue

- **WHEN** a member submits their application with a PRC License No. that has never been reviewed
- **THEN** they appear in the PRC Verification Queue even though nothing is pending

#### Scenario: Approving a pending change commits all three fields together

- **WHEN** an administrator Approves a member with pending PRC field changes
- **THEN** `PrcLicenseNo`, `PrcRegistrationDate`, and `PrcValidUntilDate` are each updated to their
  pending values (where a pending value exists), all pending fields are cleared, and
  `PrcIdVerified` becomes `true`
- **AND** an Approved entry is recorded in the audit trail

#### Scenario: Approving a never-reviewed PRC License No. just marks it verified

- **WHEN** an administrator Approves a member who has no pending change but has never been
  verified
- **THEN** the current PRC fields are unchanged and `PrcIdVerified` becomes `true`

#### Scenario: Rejecting a pending change discards it and leaves the current values standing

- **WHEN** an administrator Rejects a member's pending PRC field changes with a reason
- **THEN** all pending PRC fields are cleared and the current values are unchanged
- **AND** the member sees the rejection reason on their Personal Information tab
- **AND** a Rejected entry (with the reason) is recorded in the audit trail

#### Scenario: Requesting additional documents sets the reupload flag

- **WHEN** an administrator selects Request Additional Documents with a reason
- **THEN** `PrcIdReuploadRequested` is set and any pending PRC field values are cleared
- **AND** the member sees the reason on their Personal Information tab, distinct from a rejection
- **AND** a MoreDocumentsRequested entry (with the reason) is recorded in the audit trail

#### Scenario: Rejecting a never-reviewed member keeps them in the queue

- **WHEN** an administrator Rejects a member who had no pending change (a first-time review)
- **THEN** the member remains in the PRC Verification Queue (still unverified) with the reason
  attached, until they submit a new attempt or an admin later approves

#### Scenario: No bypass path exists

- **WHEN** an administrator edits a member via the generic `PUT /api/members/{id}` form
- **THEN** there is no field there to directly set `PrcIdVerified` - only Approve (via the PRC
  Verification detail page) can change it, ensuring every decision is captured in the audit trail
