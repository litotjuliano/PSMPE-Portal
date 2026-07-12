# prc-verification Specification (Delta)

## ADDED Requirements

### Requirement: Applicant Submission Collects PRC Fields Without AI Involvement

At both entry points (initial registration wizard, and a post-approval PRC License No. change
from My Profile), the applicant SHALL enter Full Name (via existing name fields), PRC License
(Registration) No., Registration Date, and Valid Until date, and upload a copy of the PRC ID
(reusing the existing JPG/PNG/PDF-under-2MB constraints). Submission SHALL succeed with standard
field validation only (required/format checks) - no AI extraction or comparison SHALL run at
submission time.

#### Scenario: Submission succeeds without any AI call

- **WHEN** an applicant fills in Registration Date, Valid Until, and PRC License No., uploads a
  PRC ID, and submits
- **THEN** the submission succeeds immediately based on standard field validation only
- **AND** no extraction or comparison has occurred yet
- **AND** the application is visible in the existing PRC Verification Queue (from
  `add-profile-data-continuity`)

#### Scenario: Registration Date and Valid Until are optional

- **WHEN** an applicant submits without filling in Registration Date or Valid Until
- **THEN** the submission still succeeds, consistent with PRC License No. itself already being
  optional-to-submit

### Requirement: AI Extraction Is Admin-Triggered, Never Automatic

An administrator reviewing a member's pending PRC verification SHALL explicitly trigger AI
extraction (an explicit action, not automatic on page load - see `proposal.md` Open Decision 1).
Extraction SHALL read the member's currently-uploaded PRC ID document and use a vision-capable LLM
to extract: Name, Registration No., Registration Date, Valid Until, License Status, and
Profession.

#### Scenario: Admin explicitly triggers extraction

- **WHEN** an administrator opens a member's PRC verification detail page and clicks "Run AI
  Verification"
- **THEN** the system reads the member's current PRC ID document and calls the extraction service
- **AND** a loading state is shown until the result (or failure) returns

#### Scenario: Opening the detail page alone does not call the AI

- **WHEN** an administrator merely opens a member's PRC verification detail page without clicking
  the run action
- **THEN** no extraction call is made and no `PrcVerificationRun` is created

### Requirement: Extraction Failure Falls Back to Manual Review

If the extraction service call fails (provider error, timeout, unreadable image, etc.), the system
SHALL show "Extraction unavailable - verify manually" and SHALL still allow the administrator to
Approve, Reject, or Request Additional Documents without an AI result.

#### Scenario: Extraction service failure does not block a decision

- **WHEN** the AI extraction call fails for any reason
- **THEN** the admin sees "Extraction unavailable - verify manually"
- **AND** Approve/Reject/Request Additional Documents remain available
- **AND** a `PrcVerificationRun` is still recorded with the failure flagged (see the persistence
  requirement below), so the failed attempt is part of the audit trail

### Requirement: Field Comparison Rules

The system SHALL compare each extracted field against the corresponding applicant-entered value
(or, for License Status and Profession, against expected constants) using these rules:

- **Name**: fuzzy match - case-insensitive, word-order-tolerant, punctuation-stripped - expressed
  as a similarity percentage.
- **Registration No.**: strict match after normalization (trim, strip spaces/dashes) - either
  100% (exact match) or 0% (any difference).
- **Registration Date / Valid Until**: parsed from whatever format the AI extracts (e.g. "March
  15, 2022" or "03/15/2022") into a date, then compared for an exact match against the
  applicant-entered date - no fuzziness once parsed.

#### Scenario: Name comparison tolerates case, order, and punctuation

- **WHEN** the applicant entered "Juan Dela Cruz" and the AI extracted "DELA CRUZ, JUAN"
- **THEN** the comparison normalizes both (case-insensitive, punctuation-stripped, order-tolerant)
  before computing a similarity percentage, rather than penalizing the name for order/punctuation
  differences alone

#### Scenario: Registration No. comparison is strict

- **WHEN** the applicant entered "0123456" and the AI extracted "0123-456"
- **THEN** both are normalized (spaces/dashes stripped) before comparing
- **AND** the result is exactly 100% if they match after normalization, or 0% if they still differ

#### Scenario: Dates compare after parsing regardless of format

- **WHEN** the applicant entered a Valid Until of `2027-03-14` and the AI extracted "MARCH 14,
  2027"
- **THEN** the extracted text is parsed into a date and compared directly against the
  applicant-entered date, rather than doing a literal string comparison

### Requirement: Independent Expiry and License Status Checks

Regardless of whether Valid Until matches the applicant-entered value, the system SHALL
independently check that the extracted Valid Until date is in the future (flagging an expired
license even if the dates otherwise match), and that the extracted License Status reads
REGISTERED (case-insensitive). The extracted Profession SHALL be compared against an expected
value (see `proposal.md` Open Decision 3) and flagged if different.

#### Scenario: An expired license is flagged even if dates match

- **WHEN** the applicant-entered Valid Until matches the extracted Valid Until exactly, but that
  date is in the past
- **THEN** the system flags the license as expired independently of the date-match result

#### Scenario: A non-REGISTERED status is flagged

- **WHEN** the extracted License Status is anything other than REGISTERED (case-insensitive)
- **THEN** the system flags this as a failed independent check

#### Scenario: An unexpected Profession is flagged

- **WHEN** the extracted Profession does not match the expected value
- **THEN** the system flags this as a discrepancy for the admin to review

### Requirement: Image Quality Assessment

Each extraction run SHALL include an image quality assessment of Good, Fair, or Poor. A Poor
assessment SHALL be surfaced with a recommendation to ask the applicant to re-upload a clearer
image.

#### Scenario: Poor image quality recommends re-upload

- **WHEN** an extraction run assesses the uploaded PRC ID image quality as Poor
- **THEN** the result display recommends re-upload
- **AND** the admin can act on this via the Request Additional Documents action

### Requirement: Overall Confidence and Recommendation

Each extraction run SHALL compute an overall confidence percentage and a recommendation (Approve,
Manual Review, or Reject) from the per-field results, the independent checks, and image quality
(see `proposal.md` Open Decision 2 for the exact weighting/thresholds). A Registration No.
mismatch, an expired license, or a non-REGISTERED status SHALL always prevent an Approve
recommendation regardless of how high other fields score.

#### Scenario: A Registration No. mismatch prevents a recommend-approve

- **WHEN** the Registration No. does not match, even though Name/dates/status all score well
- **THEN** the overall recommendation is not Approve

#### Scenario: High confidence across the board recommends Approve

- **WHEN** Name, Registration No., both dates, License Status, and image quality all score well
  and the license is not expired
- **THEN** the overall recommendation is Approve

### Requirement: Verification Runs Are Persisted and Append-Only

Every extraction attempt (successful or failed) SHALL be persisted as a new `PrcVerificationRun`
record - raw extracted values, per-field match results, image quality, overall confidence,
recommendation, timestamp, the provider/model used, and which admin triggered it. Re-running
verification SHALL append a new run; it SHALL NOT overwrite or delete a prior run.

#### Scenario: Re-running verification appends rather than overwrites

- **WHEN** an administrator runs verification twice for the same member (e.g. after the applicant
  re-uploads a clearer document)
- **THEN** two separate `PrcVerificationRun` records exist afterward
- **AND** both remain queryable as part of that member's verification history

### Requirement: Admin Decisions - Approve, Reject, Request Additional Documents

From the PRC verification detail page, an administrator SHALL be able to Approve (per the
existing `add-profile-data-continuity` Admin PRC Verification Queue mechanics), Reject with a
reason (ditto), or **Request Additional Documents** with a reason - a new action for when the
entered text is correct but the uploaded image needs to be redone. Each decision SHALL be recorded
in the existing `PrcVerificationHistory` audit trail, optionally referencing the
`PrcVerificationRun` that informed it.

#### Scenario: Request Additional Documents does not require the member to retype an unchanged value

- **WHEN** an administrator selects Request Additional Documents for a member whose entered PRC
  License No. was correct
- **THEN** the member is prompted to re-upload the PRC ID document only
- **AND** submitting a new document without changing PRC License No. text still results in a new
  review being triggered (see the `member-profile` delta's `PrcIdReuploadRequested` mechanic)

#### Scenario: Every decision is recorded in the audit trail

- **WHEN** an administrator Approves, Rejects, or Requests Additional Documents
- **THEN** a `PrcVerificationHistory` entry is recorded with the decision, reason (if applicable),
  deciding admin, and timestamp

### Requirement: PRC Verification Detail Page

Each member in the PRC Verification Queue SHALL have a dedicated detail page showing the
applicant-entered values and the uploaded PRC ID document. After an extraction run, the page
SHALL show entered vs. extracted values side by side, per-field match results, image quality,
overall confidence, and the recommendation.

#### Scenario: Detail page shows entered values and document before any AI run

- **WHEN** an administrator opens a member's PRC verification detail page for the first time
- **THEN** the applicant-entered values and the uploaded PRC ID document are shown
- **AND** no extraction results are shown yet, since none has been requested

#### Scenario: Detail page shows side-by-side comparison after a run

- **WHEN** an administrator has run AI verification for a member
- **THEN** the page shows entered vs. extracted values per field, each field's match result,
  image quality, overall confidence, and the recommendation
