# Members

## Purpose

A PSMPE professional membership profile — distinct from the `Users` concept in `auth.md`/
`roles.md`. `ApplicationUser` is the login/role account (shared by staff roles like Admin/
Manager/Accounts too); `Member` is specifically a professional-membership record (PRC license,
membership number, chapter, dues, renewal date). Every `Member` has exactly one linked
`ApplicationUser` (1:1, required, enforced by a unique index on `UserId`), but not every
`ApplicationUser` has a `Member` profile.

Sourced from the real product spec (`psmpe web portal.pptx`), scoped down for this iteration —
see Open questions/TODO for what's deferred.

## Endpoints

- `GET /api/members` — paged/sorted list of member profiles
  - Auth: `members:view` permission (deliberately *not* also gated by the `RequireAdmin` role
    policy — that would block Manager/Accounts, who are granted `members:view` but aren't
    Admin/Super Admin by role; see Authorization rules)
  - Query: `page`, `pageSize`, `sortBy` (`lastName` | `membershipNo` | `chapter` | `status` |
    `submittedAt`), `sortDir`, `status` (optional `MembershipStatus` filter), `pendingApprovalOnly`
    (optional bool — `true` returns only members with `ApprovedAt == null`; see the
    Draft/Approval/Status split below for why this is a separate filter from `status`),
    `pendingPrcVerificationOnly` (optional bool — a proposed RMP licence change awaiting a decision,
    or one never reviewed at all), `search` (optional — case-insensitive substring match against
    first/last name, Membership No., or email; backs the All Members tab's search box)
  - The three filters back the three tabs of the consolidated Members page — see "One Members page,
    three tabs" below. `submittedAt` sorting exists so the approval queue can read oldest-first.
  - **Always excludes drafts** (`SubmittedAt == null`) regardless of other filters — an
    in-progress, not-yet-submitted application is invisible here, not just unapproved.
  - Response: `PagedResult<MemberDto>`
- `GET /api/members/stats` — aggregate counts for the Admin/staff Dashboard's Membership
  statistics section
  - Auth: `members:view` permission (same as the list endpoint above)
  - No query parameters — always computed over the same base set `GET /api/members` uses
    (submitted, non-draft members, with system/staff accounts excluded)
  - Response: `MemberStatsDto` — `StatusCounts` (Pending/Active/Expired/Deactivated, all four
    always present even at zero), `RegistrationTrend` (last 12 calendar months, oldest first,
    zero-filled gaps), `ByChapter`/`ByMemberType` (one row per `Chapters.All`/`MemberTypes.All`
    constant, in that declared order, zero-filled), and `ActionItems` (`PendingApprovals` —
    `ApprovedAt == null`; `PendingPrcVerification` — same predicate as `pendingPrcVerificationOnly`
    above; `RenewalsDueSoon` — Active members whose `RenewalDueDate` falls within 60 days)
  - Not cached — this is one dashboard load, not a hot path like the grace-period lookup
- `GET /api/members/{id}` — get one member profile
  - Auth: `members:view` permission
- `GET /api/members/me` — the caller's own member profile
  - Auth: authenticated (any role)
  - Returns `404` if the caller hasn't started their application at all yet. Once started (even
    mid-wizard), returns the draft with `submittedAt: null` — the frontend distinguishes "no
    profile" from "draft in progress" from "submitted" by checking for a 404 vs. `submittedAt`,
    not by a second endpoint.
- `POST /api/members` — admin creates a member profile for an existing user
  - Auth: `members:manage` permission
  - Request: `{ userId, membershipNo?, firstName, middleName, lastName, suffix, birthdate, gender, civilStatus, educationLevel, schoolName, courseYearGraduated, specifiedProfession, mobileNumber, houseNo, street, barangay, cityMunicipality, province, zipCode, mailingHouseNo, mailingStreet, mailingBarangay, mailingCityMunicipality, mailingProvince, mailingZipCode, prcLicenseNo, prcRegistrationDate, prcValidUntilDate, ptrNumber, tin, chapter, company, memberType, renewalDueDate, nationalDuesReferenceNo }`
    (see "Membership Application Form fields" below for what each new field maps to on the paper
    form, and the "RMP" naming note)
  - `membershipNo` is optional — PSMPE assigns its own control number, and an admin creating a
    profile out of band may not have it yet (see "Membership number lifecycle" below). `409` if a
    non-blank value collides with an existing member's.
  - **`prcLicenseNo` is required** (`400` if blank), matching what `POST /me/submit` already demands
    of self-service applicants. Without one a member never enters the verification queue and, since
    approval now requires verification, could never be approved — see "RMP verification gates
    approval" below.
  - Does **not** create a login account — that's `POST /api/auth/register`'s job. `400` if
    `userId` doesn't exist; `409` if that user already has a profile.
  - Always starts as `Status: Pending`, `ApprovedAt: null`, `SubmittedAt: now` — admin-entered
    profiles skip the draft phase entirely, since they're complete the moment they're created.
- `PUT /api/members/{id}` — admin edit, including `Status`, `MemberType`, and `MembershipNo`
  - Auth: `members:manage` permission
  - `MembershipNo` in this request is the correction path for a control number mistyped at
    approval — blank/omitted leaves the stored value untouched (never clears an assigned number);
    a non-blank value is validated for length and uniqueness the same as at approval.
- `PUT /api/members/me` — self-service edit/autosave of the caller's own profile
  - Auth: authenticated (any role)
  - Request: same shape as create, minus `userId`/`membershipNo`/`Status` — those are
    business-controlled, not self-service (`MemberType` *is* self-service — it's chosen at
    registration, not a business decision like `Status`)
  - **Upserts**: creates the profile (`MembershipNo: null`, `Status: Pending`, `SubmittedAt: null`)
    if the caller doesn't have one yet — see "Membership number lifecycle" below for why this no
    longer generates a number. This is the wizard's per-step autosave mechanism — every "Save &
    Continue" click calls this with whatever's been filled in so far, so closing the browser
    mid-wizard and returning later resumes with everything intact. Does *not* set `SubmittedAt` —
    that's a separate, explicit action (below).
- `POST /api/members/me/submit` — self-service: finalizes the draft into a submitted application
  - Auth: authenticated (any role)
  - `404` if the caller has no draft at all (hasn't saved anything yet); `400` if
    `FirstName`/`LastName`/`Chapter`/`MemberType` are still empty (lists what's missing);
    otherwise sets `SubmittedAt` to now if not already set (idempotent).
  - This is what makes the application visible to admins at all — see Draft/Approval/Status below.
- `POST /api/members/{id}/approve` — admin marks an application as reviewed, assigning PSMPE's
  membership control number in the same step
  - Auth: `members:manage` permission
  - **`400` if the member's RMP licence has not been verified** (`PrcIdVerified == false`). The
    licence is the eligibility criterion for membership, and approving issues a control number,
    generates a receipt and emails the member — none of it cheap to unwind. See "RMP verification
    gates approval" below.
  - Request: `{ membershipNo }` — **required**. `400` if blank/whitespace-only or over 32
    characters; `409` if another member already holds it. Neither approves the application — the
    caller must resolve the number and retry.
  - Sets `ApprovedAt` to now and `MembershipNo` to the trimmed request value, if not already
    approved; idempotent (approving an already-approved application is a no-op success and
    **does not** re-assign `MembershipNo` — a repeat call, e.g. from the receipt/email retry path,
    must never silently renumber a live member).
  - **Also accepts the registration payment, in the same transaction** — see "Approval and payment
    are one act" below. `Status` therefore becomes `Active` as part of approving, and the request
    carries an optional `payment` block for members who have none on record.
- `DELETE /api/members/{id}` — admin removes just the member profile
  - Auth: `members:manage` permission
  - Leaves the underlying login/role account intact — retiring someone's membership record
    doesn't delete their system account.
  - `400` if the member has any RMP/PRC verification history on record (`Restrict` FK on
    `PrcVerificationHistories`) — a clean rejection instead of a raw `DbUpdateException`.
  - Contrast with deleting the *User* account entirely (`DELETE /api/admin/users/{id}`, Super
    Admin only - see `roles.md`), which cascades to remove this `Member` row too (plus
    `MemberUploads`/`MemberCertificates` and their files), and is blocked the same way (`409`
    there, since it's checked before the User row itself is touched).

## Draft vs. Submitted vs. Approved vs. Status

Four separate axes on the same row, easy to conflate:

- **`SubmittedAt`** — has the applicant finished the wizard? `null` while the wizard's per-step
  autosave (`PUT /me`) has created a row but the applicant hasn't reached the final "Submit"
  step (`POST /me/submit`) yet. A draft is invisible to every admin-facing query
  (`GET /api/members`, every Members tab, notifications) — it isn't a member yet, just a
  half-filled form.
- **`ApprovedAt`** — has an admin reviewed a *submitted* application? `null` until a
  `POST /api/members/{id}/approve` call. Independent of `Status`.
- **`MemberType`** (`Domain.Enums.MemberTypes`, const list like `Chapters`) — a category chosen at
  registration (currently only `"Regular Member"`). Purely descriptive, not a workflow state.
- **`Status`** (`MembershipStatus`: `Pending`/`Active`/`Expired`/`Deactivated`) — payment-gated.
  Per the business rule: approved members who pay dues become `Active`; approved-but-unpaid
  members *stay* `Pending`. **Verifying a payment is what makes that transition** (see
  `payments.md`); `PUT /api/members/{id}` can still set `Status` by hand for corrections.

Because an approved-but-unpaid application still has `Status: Pending`, the Pending Approval tab
and the notification bell filter on `pendingApprovalOnly=true` (`ApprovedAt == null`), not
`status=Pending` — otherwise already-approved members would never disappear from the "needs
review" list. And because `GetAllAsync` unconditionally excludes drafts, an in-progress
application never shows up there either, regardless of which filters are passed.

## RMP verification gates approval

**A member cannot be admitted to PSMPE until their RMP licence has been verified.**
`MemberService.ApproveAsync` refuses when `PrcIdVerified` is false. The licence is the eligibility
criterion, and approving assigns a control number, generates a receipt and emails it — all awkward
to reverse if the licence later turns out not to check out.

Three things make this workable rather than merely restrictive:

- **The gate sits *after* the already-approved short-circuit.** Members approved before this rule
  existed keep their approval, and a repeat call on them still succeeds. The rule is forward-only,
  by design — at the time it shipped, 4 of 5 approved members held unverified licences.
- **`prcLicenseNo` is required at admin create.** `GetAllAsync`'s verification filter only matches
  members with a licence number (current or pending), so one created without any would have been
  invisible to the queue *and* blocked from approval — permanently unapprovable. Requiring it closes
  that at source. Legacy rows in that shape still exist and are still excluded from the queue.
- **`ApproveApplicationWizard` puts both decisions in one flow**, so the gate doesn't become a round
  trip between two tabs: step 1 reviews the licence against the uploaded RMP ID (Verify or Reject),
  step 2 collects the Membership ID, step 3 confirms. An already-verified member starts at step 2 —
  re-verifying would write a meaningless extra `PrcVerificationHistory` row. Rejecting at step 1
  ends the flow with the application unapproved and the reason recorded.

The standalone **RMP Verification tab remains** for the other case it serves: an existing, already
approved member changing their licence details, where no membership decision is involved.

## Membership number lifecycle

`MembershipNo` is **admin-assigned, not system-generated** — PSMPE issues its own control numbers,
and the portal never invents one. It is `null` from first wizard autosave through submission and
stays `null` until an admin supplies it at approval (`POST /api/members/{id}/approve`); the field
is `HasMaxLength(32)` but nullable despite carrying a unique index, since Postgres treats every
`NULL` as distinct — unapproved applicants never collide with each other while awaiting a number.


### Uniqueness is case-insensitive

Comparison ignores letter case at **both** layers, so `PSMPE-001` and `psmpe-001` are one number,
not two:

- `MemberService.MembershipNoExistsAsync` compares on `lower(...)`, and is used by the approve,
  create, and correction paths alike.
- The database enforces it with a unique index on `lower("MembershipNo")`, created in raw SQL by
  the `MembershipNoCaseInsensitiveUnique` migration. EF cannot express a functional index, so it is
  deliberately **not** declared in `MemberConfiguration` — restoring a plain `HasIndex` there would
  make case-differing duplicates insertable again.

The index matters independently of the service check: that check can always lose a race with a
concurrent approval, and a byte-comparing index would happily accept the loser.

### Validation at approval

`ApproveAsync` rejects a blank or whitespace-only ID, one over 32 characters, and one already held
by another member (`409 Conflict`). Format is deliberately unvalidated beyond length — PSMPE's
numbering scheme is theirs to decide, so the field stays free text.

`GET /api/members/membership-no/availability?value=…&excludeMemberId=…` (same `Members.Manage`
permission as approving, since it reports whether a number is taken) backs a debounced live check
in the dialog, so an admin sees "already assigned" while typing rather than after a rejected
submit. It is **advisory only**: responses are discarded if the typed value has moved on, a failed
lookup never blocks approving, and `ApproveAsync` re-checks regardless.

The admin UI collects it in step 2 of `ApproveApplicationWizard` at the moment of approval, on both
the Pending Approval tab and the member detail page, rather than as a form field filled in ahead of
time. If a number is mistyped, `PUT /api/members/{id}` is the only
correction path — re-approving is idempotent and deliberately will not overwrite it (see above), so
a wrong number is otherwise stuck. Every display site (all three Members tabs, the member detail
page, the member's own profile) falls back to "Not yet assigned" rather than a blank cell.

This replaced a `GenerateMembershipNoAsync` scheme that computed max-existing-plus-one and assigned
it at first wizard autosave — before the applicant had even submitted. Members saw a number the
society had never issued from the moment they started the form, every abandoned draft permanently
consumed one, and the generator materialized the entire `MembershipNo` column client-side on every
registration (`O(n)`, check-then-insert with no lock or retry). Deleted outright rather than fixed.

## Grace period

`MemberDto.IsInGracePeriod` is computed (not stored): `true` when `Status == Active`,
`RenewalDueDate` has passed, and today is still within `RenewalDueDate + MembershipGracePeriodDays`
(a `SystemConfig` row, default `30`, read by `MemberService` with an in-code fallback if unseeded).
Gives lapsed-but-recent members a window of continued access rather than an immediate cutoff.
Nothing currently enforces reduced access during grace — Certificates, CPD and Events now exist
(see `openspecs/events.md`) but none of them are gated by grace-period status specifically, only by
the harder `Expired` cutoff below. This flag remains exposed for the frontend to act on if a
grace-specific restriction is ever wanted.

## Registration: simple sign-up now, resumable application wizard later

Sign-up and the membership application are two separate, decoupled flows:

- **`RegisterPage`** (`/register`, public) is a plain one-step form — Email, Password, Confirm
  Password, Display Name, optional Username. Calls `POST /api/auth/register` only, then redirects
  to the dashboard (`/`), logged in. No Member profile is created at this point.
- **`MyProfilePage`** (`/profile`, authenticated) hosts the actual 4-step application wizard from
  the product spec (Personal Info → Contact Info → Additional Info → Payment Details), via
  `MembershipApplicationWizardCard`, whenever the caller has no Member profile yet or has one with
  `submittedAt: null` (still a draft) — once `submittedAt` is set, this page instead shows
  `MyProfileTabsCard`, five independently-editable tabs grouped by concern (Personal /
  Professional & Licensing / Contact / Account & Security / Documents & Certificates) rather than
  the wizard's own step grouping. Account & Security is also where the caller's Display Name and
  password live — the same self-service settings any account has, not membership data, surfaced
  here rather than on a separate page.
  - **Autosave/resume**: every "Save & Continue" click calls `PUT /api/members/me` with whatever's
    been filled in so far, then advances the step — so leaving mid-wizard and coming back later
    resumes with that data intact. Resume position is derived, not stored (see
    `furthestStepReached`/`hasCompleted*Info` in `MyProfilePage.tsx`): resumes at the first step
    whose own required fields aren't all filled yet - Personal Info (name/chapter/member type/etc.),
    then Contact Info (mobile number/residence address). Neither Additional Info nor Payment Details
    has a required Member field of its own any more (PTR Number was the last, and is now optional;
    Proof of Payment is an upload, and the terms/consent checkboxes are never persisted), so
    completing Contact Info carries the applicant straight to the last step.
  - **Step gating**: the wizard is a real `<form>` and Save & Continue is its submit button, so
    every field the server requires carries a native `required` and the browser blocks the step
    before `onSubmit` runs. Uploads and cross-field rules (age ≥ 18, formats) are checked in
    `validateStep` on top of that. The stepper circles only ever navigate *backwards*
    (`target > maxStepReached` is refused), so they can't be used to skip a step's requirements.
  - **Contact Information step** (2nd) opens with a read-only display of the caller's account email
    — it sits with the other ways to reach the member rather than on Personal Information, and is
    changed from Account & Security, never here.
  - **Additional Information step** (3rd) collects PTR Number, PTR Place Issued, PTR Date Issued,
    TIN, and Company. **All five are optional** — this step no longer gates submission at all.
  - **Final step** ("Payment Details") shows the membership fee/payment instructions, collects the
    Proof of Payment upload, then a review summary + terms checkbox; Submit calls
    `POST /api/members/me/submit`, which is what actually makes the application visible to admins.
  - `DashboardPage` shows a "Complete your membership application" banner (checks
    `GET /api/members/me`, shown whenever there's no profile yet or `submittedAt` is still null)
    linking to `/profile` — this is what surfaces the resumable wizard from the dashboard.
- Map-based address picker shown in the mockup is not collected — no Maps API integration exists.
  Instead, Region/Province/City are cascading dropdowns backed by bundled PSGC reference data, with
  the ZIP auto-filled on city selection (see "Address entry" below); House No./Street/Barangay
  remain free text. Photo and RMP ID are collected in
  Personal Info; Proof of Payment is collected in the final Payment Details step - all three via
  the member-scoped upload endpoints (see "File uploads" below), and all three are required to
  submit (`POST /api/members/me/submit`), along with education, profession, RMP license/dates, and
  the full residence address (House No. excepted — some Philippine addresses genuinely don't have
  one). There is no separate 2x2/formal-photo upload — the one required Photo doubles as the
  ID-print photo, so the UI only ever asks for a single photo.

### Membership Application Form fields

Reworked from a single free-text `Address` to match every field the official paper "PSMPE
Membership Application Form" asks for:

- **RMP vs. PRC naming**: the paper form's "RMP License No." is the *same* underlying field as
  the original `PrcLicenseNo` — relabeled in every user-facing string (wizard, profile, admin
  tables, the RMP Verification tab, notifications), but every internal identifier (`PrcLicenseNo`,
  `PrcIdVerified`, `PendingPrcLicenseNo`, `PrcVerificationRejectedReason`, the
  `pendingPrcVerificationOnly` filter, `UploadKind.PrcId`) deliberately keeps its original name —
  renaming the actual
  properties/columns/endpoints would be a large, purely-cosmetic ripple with no functional benefit.
- **Membership type**: `Regular Member` / `New` / `Renewal` / `Senior Citizen`. `Regular Member` is
  the original sole option and stays in the list purely because every member predating the other
  three carries it — removing it would leave those records showing a value their own edit form
  doesn't offer. Nothing validates against `MemberTypes.All`; the column is free text (max 64), so
  these constants only drive the dropdowns and the seeder. **Known limitation:** New/Renewal
  describe the *application* while Senior Citizen describes the *person*, so a senior renewing can
  only express one. Accepted deliberately to match the paper form; revisit if it causes trouble.
- **Chapter officer**: `ChapterYear` (int) and `ChapterPosition` — an optional record of an officer
  post held in the member's chapter, shown for *every* chapter, not just NCR. Named `Chapter*`
  because `Position` already means the member's employment position. Unlike `Chapter`/`MemberType`
  these are **not** locked post-submission: they describe a role the member holds rather than their
  eligibility, so someone elected mid-term records it themselves. `ChapterYear` is range-checked
  (1900–2200) on the self-service path only, matching how `YearsOfPractice` is handled — it's a
  sanity guard, not protection against a DbUpdateException.
- **Contact**: `MobileNumber` and `HousePhone` only. Both reformat as you type
  (`formatPhMobile`/`formatPhLandline` in `core/utils/memberFields.ts`) — mobile strips to a leading
  `+` plus digits, capped by prefix (12 for `63…`, 11 for `09…`, so a country-code number doesn't
  lose its last digit); landline groups into `(02) 8123 4567` / `(032) 255 1234`, inferring the area
  code from the trunk prefix (`02` is NCR, the only single-digit one). Both are idempotent — they
  strip to digits first — which is what makes them safe on every keystroke. The landline grouping is
  a heuristic, not a lookup table, so the field stays free text and the server still validates on
  digit count (7–11). The wizard/profile/admin form briefly also
  collected `Website`, `FacebookUrl`, `LinkedInUrl`, `XUrl` and `InstagramUrl`; those five were
  dropped (entity, DTOs, columns, and the URL-format validation that existed solely for them) —
  the paper application form never asked for them and no member had ever filled one in.
- **Residence address**: `HouseNo` (optional), `Street`, `Barangay`, `CityMunicipality`, `Province`,
  `ZipCode`, `Country` — replaces the old single `Address` string.
- **Mailing address**: `MailingHouseNo`, `MailingStreet`, `MailingBarangay`,
  `MailingCityMunicipality`, `MailingProvince`, `MailingZipCode`, `MailingCountry`. The
  wizard/profile offer a client-side-only "Same as Residence Address" checkbox that copies the
  residence values across at save time; there's no stored flag, just seven independent columns.
  Because there's no flag, its initial state is **inferred** by `mailingMirrorsResidence`
  (`core/utils/memberFields.ts`), shared by the wizard and the profile Contact tab: ticked when the
  mailing address is blank or already an exact copy of the residence one, unticked otherwise.
  Defaulting it to ticked unconditionally would silently overwrite a deliberately-different mailing
  address on the member's next save.
- **Education**: `EducationLevel` ("Technical School" / "College / University"), `SchoolName`,
  `CourseYearGraduated` (free text, e.g. "BSCE 2023").
- **Profession**: `SpecifiedProfession` ("Master Plumber" / "Other Professional Related").
- **RMP license dates**: `PrcRegistrationDate`, `PrcValidUntilDate` (plain `DateOnly?` data entry —
  not the deferred AI/OCR extraction proposal). Valid Until auto-fills to **one year after** the
  registration date, on all three surfaces, but stays editable: `shouldDeriveValidUntil`
  (`core/utils/memberFields.ts`) only overwrites when it's blank or still holds exactly what the
  *previous* registration date derived, so a hand-typed date is never clobbered. The derivation is
  string math, not `Date.setFullYear` + `toISOString`, which lands a day early in any timezone east
  of UTC; a 29 Feb registration clamps to 28 Feb rather than rolling into March. Staged the same way `PrcLicenseNo` already was:
  changing the license number **or** either date requires a fresh RMP ID re-upload, and stages all
  three together as `PendingPrcLicenseNo`/`PendingPrcRegistrationDate`/`PendingPrcValidUntilDate`
  until an admin approves or rejects via the existing PRC Verifications queue.
- **PTR issuance**: `PtrPlaceIssued` and `PtrDateIssued` (`DateOnly?`). All three PTR fields —
  including `PtrNumber`, which used to be required — are optional. Dropping the requirement left
  the Additional Information step with no submit gate of its own, so it also stopped being a
  distinct resume gate; `hasCompletedAdditionalInfo` was removed rather than left always-true.
- **Age** is derived from `Birthdate` for display only — never persisted.
- **Data Privacy Consent**: a second wizard checkbox (RA 10173 text + `privacy.gov.ph` link)
  alongside the existing membership-terms checkbox — same pre-existing pattern as that checkbox
  (client-side Submit gate only, not persisted to the `Member` record).

### Address entry

All three address surfaces (registration wizard, profile Contact tab, admin member form) render one
shared `PhilippineAddressFields` component rather than the three hand-duplicated copies they used
to be. Region → Province → City → Country are **type-to-search** boxes (`SearchableSelect`, a thin
wrapper over a native `<datalist>`); picking a city auto-fills ZIP.

- **Why `<datalist>` rather than a custom combobox**: type-to-filter, keyboard navigation and
  screen-reader semantics for free, no dependency, and no focus/outside-click handling to get
  wrong. It doesn't constrain input — which matches the free-text-allowed decision already made for
  these fields; a value outside the list is an address the dataset is missing, not an error.
- **Province is not gated on Region.** With no region picked the box offers *all* provinces
  (`getAllProvinces`), and choosing one back-fills the region via `findRegionFor` — someone who
  knows their province shouldn't have to work out its region first. City still needs a province.

- **Reference data is bundled, not an API.** `apps/web/src/data/ph-locations.json` (PSGC-derived:
  17 regions, 87 provinces, 1,647 cities/municipalities) plus a ~216-entry `countries.json`, both
  behind a dynamic `import()` in `core/utils/phLocations.ts` so they build as separate chunks
  (~19KB gzipped for locations, ~1.3KB for countries) fetched only when an address form renders —
  an admin browsing `/members` never pays for them. There is no `GET /api/locations/*` endpoint;
  the data is static, admin-unmanaged, and the cascade needs it in memory anyway. This matches the
  existing `Chapter`/`MemberType` precedent (reference data lives in the codebase, not fetched).
- **Region is not stored.** It exists only to narrow the province list. `findRegionFor` recovers it
  from the saved province/city when an existing profile is opened — without that, every member who
  registered before this shipped would see empty dropdowns and think their address was wiped. A
  saved value that matches nothing in the reference data is kept as a selectable option and
  flagged, never silently dropped.
- **ZIP auto-fills but is never locked** — 76 of 1,647 cities have no ZIP on record, and some
  cities legitimately have several. Selecting a city with no mapped ZIP leaves the field alone
  rather than blanking it.
- **`Country` defaults to "Philippines"** but is a full dropdown, since an overseas-resident member
  is plausible. It's in the submit-required set (`MailingCountry` isn't, matching the other mailing
  fields); in practice the default means this never blocks a wizard user — it's a guard against a
  caller that skips the field entirely.
- **NCR quirk**: the source data models NCR's "province" level as PSGC districts (City of Manila
  splits into 30 ZIP-bearing entries — Tondo I/II, Binondo, Quiapo…). That's the dataset's real
  structure, not a defect.

## File uploads (photo, RMP ID, proof of payment, etc.)

Files are **not** stored in Postgres, and are **not** served as plain static URLs - both were
deliberate calls, not the obvious defaults:

- **`MemberUpload`** (`Domain.Entities`) is a thin pointer row - `UserId`, `Kind`
  (`Photo`/`PrcId`/`ValidGovernmentId`/`Signature`/`ProofOfPayment`/`Receipt`), `StorageKey`,
  `ContentType` - a few dozen bytes, regardless of the file's actual size. Keyed by `UserId`, not
  `MemberId`, so a photo/RMP ID can be uploaded before any
  `Member` row exists yet (before Personal Info is saved). One row per `(UserId, Kind)` - a
  re-upload overwrites the pointer (and the file at the same storage key), no accumulation.
  Storing the bytes directly in Postgres was considered and rejected: DigitalOcean prices managed
  Postgres storage far more expensively than object storage, and it only gets worse as photos +
  PRC IDs + eventual CPD certificates accumulate across the whole membership base - that would
  force costly compute-tier upgrades just for storage headroom.
  - **`Kind` is a string-backed enum** (`HasConversion<string>()` in `MemberUploadConfiguration`),
    not EF's default raw int. This fixed a real data-corruption incident: removing `FormalPhoto`
    from the middle of the enum shifted every later ordinal down by one, so a stale row written
    before the removal was silently reinterpreted as a *different* `Kind` once a new value
    (`Receipt`) was appended at the now-vacant ordinal - a member's PRC ID scan was served back as
    their payment receipt. The string conversion (with an explicit ordinal→name migration to
    correct already-persisted rows) makes the column immune to any future enum reordering.
- **`IFileStorageService`** (`Application.Common.Interfaces`) abstracts *where* the bytes actually
  live, behind `SaveAsync`/`OpenReadAsync` keyed by an opaque string. `LocalDiskFileStorageService`
  (`Infrastructure.Services`) is the only implementation today - writes/reads under
  `wwwroot/uploads/{key}`. **Known limitation, not yet resolved**: this won't survive a
  redeploy/restart on a platform with an ephemeral filesystem (e.g. the DigitalOcean deploy this
  repo's CI/CD targets). The seam exists specifically so a real object-store implementation (e.g.
  DigitalOcean Spaces, S3-compatible and correctly priced for this) can be swapped in later as a
  contained change - it needs real Spaces credentials to build and verify, which don't exist yet.
- **Serving is authenticated**, not a plain static file - `GET /api/members/me/{kind}`
  (self) and `GET /api/members/{id}/{kind}` (admin, `members:view` permission) stream
  bytes through `MembersController`, checking "is this the caller's own file, or an authorized
  staff member's?" first. This closes a real gap the old `app.UseStaticFiles()` approach had -
  anyone with a URL (or who guessed one) could fetch any member's RMP ID scan, no login required.

**Endpoints** (all on `MembersController`, replacing the old standalone `/api/uploads`):
- `POST /api/members/me/photo`, `.../prc-id`, `.../valid-government-id`,
  `.../signature`, `.../proof-of-payment` - `[Authorize]` only, multipart file.
- `GET /api/members/me/{kind}` (same kinds) - `[Authorize]` only, own file.
- `GET /api/members/me/receipt` - `[Authorize]` only, own file - no matching `POST`, members never
  upload this themselves (see "One Members page, three tabs" below for how it's created).
- `GET /api/members/{id}/{kind}` (same kinds, `Receipt` excluded) - `members:view` permission.

**Images are optimized, not just size-gated**: users frequently don't know how large their phone
photos are before picking one, so `.jpg`/`.jpeg`/`.png` uploads are accepted up to `24MB` raw, then
decoded, downscaled (only if needed - longest side capped at `1600px`, aspect ratio preserved,
never upscaled) and re-encoded as JPEG at quality `82` via `SkiaSharp` (this logic lives in
`MemberUploadService`, Application layer) before being handed to `IFileStorageService` - always
stored as `.jpg` regardless of the original extension. `.pdf` files (`PrcId`/`ProofOfPayment` only)
have no such optimization path and keep a stricter `2MB` hard cap - except `ProofOfPayment`, which
gets its own tighter `1MB` cap regardless of whether it's an image or a PDF, per the application
form. `Photo` (also used for ID printing) and `ProofOfPayment` also get a distinct storage-key
naming convention - `{userId}/{kind}-{surname}-{firstname}-{birthdate:yyyyMMdd}-{timestamp}.ext`
instead of the plain `{userId}/{kind}.ext` other kinds use - purely a storage-key cosmetic
difference; the DB row is still upserted by `(userId, kind)` exactly like every other kind.

**Why SkiaSharp, not SixLabors.ImageSharp** (the more commonly-reached-for .NET image library):
ImageSharp's license requires a paid commercial license for organizations above roughly 1 employee
or $1M revenue - not something to opt this project into silently. SkiaSharp is MIT-licensed with
no such threshold. Note it needs *two* package references, not one - `SkiaSharp` alone only
bundles Windows/macOS native binaries; the Linux container this project deploys to (see
`Dockerfile`) additionally needs `SkiaSharp.NativeAssets.Linux`.

**Frontend wrinkle**: this app's auth is a JWT in localStorage, attached via `apiClient`'s
Authorization header - not a cookie. A plain `<img src="/api/members/me/photo">` can't carry that
header, so the wizard fetches the image via `apiClient` (`responseType: 'blob'`) and uses
`URL.createObjectURL(...)` as the `<img src>` instead (`uploadApi.fetchMyPhotoUrl`/
`fetchMyPrcIdUrl`, returning `null` on a `404` rather than throwing, since "nothing uploaded yet"
is an expected state). On file pick, the wizard shows an **instant local preview**
(`URL.createObjectURL(file)`, no round trip needed) while the upload happens in the background.
Object URLs are revoked on replacement/unmount to avoid leaking memory.

Still no orphan-file cleanup if a photo/PRC ID's *extension* changes between uploads (e.g. PRC ID
switching from an image to a PDF) - the storage key changes, so the previous file at the old key
is left behind. Minor, same-extension re-uploads (the common case) simply overwrite in place.

**Deployment note**: the WebAPI container runs as the base image's non-root `app` user, so the
`wwwroot/uploads` volume it writes to must be owned by that user. Docker only applies the image's
ownership to a *freshly created* named volume - an already-existing volume keeps whatever
ownership it was first initialized with, which was `root` here (predating the Dockerfile
explicitly creating this directory under `USER app`), causing every upload to fail with an
`UnauthorizedAccessException` until manually `chown`'d on the running containers (staging and
production both hit this).

## One Members page, three tabs

`MembersPage` (`/members`, Admin/Super Admin) is the single admin-facing member list. It replaced
three separate nav entries, pages and tables — Members, Membership Approvals and RMP Verifications —
which were all the *same* `GET /api/members` query with different filters, plus ~400 lines of
duplicated table markup.

| Tab | URL | Filter | Extra columns | Row actions |
|---|---|---|---|---|
| All Members | `/members` | none | Status, Email | Edit, Delete |
| Pending Approval | `/members?queue=approval` | `pendingApprovalOnly` | Applied | Approve, View |
| RMP Verification | `/members?queue=rmp` | `pendingPrcVerificationOnly` | Current / Pending RMP No. | Approve, Reject, View ID |

- **The two approvals stayed separate decisions.** Only the navigation and the table code merged.
  Membership approval admits an applicant once and requires a Membership ID; RMP verification
  recurs whenever licence details change and can be rejected with a reason that lands in
  `PrcVerificationHistory`. **A member can be waiting on both at once** — a new applicant with an
  unverified RMP number appears in both tabs, and clearing one leaves the other pending. That
  double-listing was the main argument for merging: it previously meant visiting two pages for one
  person.
- **The active tab lives in the URL** (`?queue=`) so it is linkable. `/membership-approvals` and
  `/prc-verifications` are kept as `<Navigate>` redirects rather than deleted, and the notification
  bell links to `?queue=rmp`.
- **Tab counts** come from two `pageSize: 1` calls reading `totalCount`, refetched after every
  decision. No dedicated counts endpoint — the topbar bell already queries both queues the same way,
  and a one-row response is cheap. A failed count only blanks a badge; it never surfaces as a
  page-level error.
- **One table, three views.** `MembersTable` takes `view: 'all' | 'pendingApproval' | 'pendingRmp'`.
  Name/Membership No./Chapter are shared and sortable in every view; the tail and the action column
  are per-view. `submittedAt` was added to `GetAllAsync`'s sort whitelist so the approval queue can
  order oldest-first, which is also its default.
- **Accepted trade-off**: collapsing three nav entries to one loses the standing "work is waiting"
  cue. The topbar bell still shows both queues with counts and names. A count badge on the Members
  nav item would need its own fetch inside `SideNav` and was deliberately left out.

The topbar notification bell and the dedicated
`NotificationsPage` (`/notifications`) both derive their content from the same
`pendingApprovalOnly=true` query — no separate notifications entity, no read/unread tracking (an
item simply stops matching the filter once approved). This admin-facing side is pull-based
(fetched on page load), not real-time push — no WebSocket/SignalR.

**Approving *does* notify the member**, both ways, from `MembersController.Approve` (only the one
time an application actually transitions to approved - `ApproveAsync` itself is idempotent, so a
repeat call doesn't regenerate/resend):
- `IssueApprovalReceiptAsync` renders a system-generated JPEG receipt (`ReceiptGenerator`, plain
  SkiaSharp canvas+text - Membership No./Name/Chapter/Member Type/Date Approved plus the fixed fee
  breakdown already shown in the wizard's Payment Details step) and stores it as this member's
  `UploadKind.Receipt` (same `MemberUpload` pointer-row mechanism as every other document above) -
  that's what `DashboardPage`'s `ReceiptBanner` and `GET /api/members/me/receipt` serve back.
- The same JPEG bytes are emailed to the member as an attachment via `IEmailSender` (now extended
  with an optional `attachments` parameter - `SmtpEmailSender` via MimeKit's `BodyBuilder
  .Attachments`, `ConsoleEmailSender` just logs the attachment names in dev).
- **Requires real fonts at runtime**: SkiaSharp text drawing needs an actual font file + fontconfig
  on Linux (unlike a Windows dev box, which always has one) - the WebAPI `Dockerfile`'s final stage
  installs `fontconfig`/`fonts-dejavu-core` specifically for this.

## Authorization rules

- `members:view` / `members:manage` permissions (see `roles.md`), seeded by default to Admin
  (both) and Manager/Accounts (view only) — editable afterward via `/admin/roles` like any other
  permission.
- Self-service (`/me` endpoints) requires no permission claim, only authentication — anyone can
  view/edit their *own* profile once linked, roles/permissions only gate viewing/editing *other*
  people's profiles.
- **A restricted member is blocked from every endpoint except an explicit allowlist** —
  `MembershipAccessMiddleware` (`PSMPE.Portal.WebAPI`) enforces three independent conditions, in
  this fixed order (a member failing more than one sees whichever runs first):
  1. **`MEMBERSHIP_EXPIRED`** — `Status == Expired` (past the grace period).
  2. **`MEMBERSHIP_NOT_STARTED`** — the `Member`-role account has no `Member` row at all yet:
     registered but never submitted an application (`AuthController.Register` only ever creates the
     account/role; the `Member` row comes later, from `MemberService.SubmitMyProfileAsync`). Without
     this check a brand-new, never-applied account had *unrestricted* portal access — a real
     security gap, since `member is not null && ...` on the other two checks is trivially false for
     a null `member`.
  3. **`PORTAL_ACCESS_REQUIRED`** — `!HasPortalAccess` (`Deactivated` excepted) — see `payments.md`.

  The allowlist (endpoints carrying `[AllowExpiredMember]`, a plain marker attribute the middleware
  reads off endpoint metadata, not an authorization policy) is every `/me`-prefixed action on this
  controller, plus `AccountController`'s two self-service actions, `PaymentsController`'s
  `me`/`fees`/`proof` endpoints (see `payments.md`), `AuthController`'s data-privacy-consent pair
  (reachable before a profile can exist), and — outside any of those controllers —
  `EventsController.GetAll`/`GetById`/`GetPoster` and `GET .../certificate` (see
  `openspecs/events.md`). Together: what any restricted member needs to view/complete their profile,
  pay their way back to full access, retrieve records tied to credit already earned, and *browse*
  events. Staff/admin roles (any role other than exactly `Member`) are never gated, and
  Active/grace-period members with portal access are unaffected.

  **Browsing is intentionally more permissive than acting.** Events is reachable and visible
  (`GetAll`/`GetById`/`GetPoster`) for any restricted member, but the actions that change state
  (`Register`, `SubmitPayment`, etc.) are not on the allowlist and still 403 — the frontend
  (`EventRegisterModal.tsx`) mirrors this by disabling the Register button with a message specific
  to the reason, rather than hiding the page. The frontend's `ExpiredMembershipGate` does not
  redirect or hide navigation for a restricted member at all (it did, once — that turned out to be
  confusing UX when a fully-visible page bounced back to `/profile` on click, so it was removed);
  `AppMenu`'s sidebar is role-filtered only. All of this is UX only — the middleware above is the
  actual enforcement, so a request slipping past any frontend affordance still gets a 403 from the
  API.

## Open questions / TODO

- **`IFileStorageService` only has a local-disk implementation** (see "File uploads" above) -
  won't survive a redeploy/restart in production. A `DigitalOceanSpacesFileStorageService` is the
  intended next implementation once real Spaces credentials exist; the interface seam makes that
  a contained addition, not a rewrite.
- **Chapter is a fixed const list** (`Domain.Enums.Chapters`, mirrors `RoleNames`/`Permissions`'s
  style), not a database-editable entity — no mockup or requirement showed chapter CRUD.
  Revisit as a real entity+table if admins ever need to add/rename chapters without a deploy.
- ~~**Payments/Dues domain doesn't exist yet**~~ — **built**, see `payments.md`. Verifying a payment
  is now the only thing that sets `Status = Active` or moves `RenewalDueDate`. `Status` also now
  auto-transitions `Active → Expired` once the grace period ends (`MembershipLifecycleService`,
  see `payments.md`). What remains deferred: an online payment gateway, and refunds/partial
  payments.
- No audit log for profile/status changes yet (same gap noted for role changes in `roles.md`) —
  `AuditLog` exists for other events (rate-limit rejections, lockouts, membership approvals), see
  `system-logs.md`; profile/status changes just aren't wired into it.
- **Semi-automated, AI/OCR-assisted PRC License verification is a deferred future feature** - a
  full OpenSpec proposal already exists at `openspec/changes/add-prc-ai-verification/` (admin-
  triggered vision-LLM extraction from the uploaded PRC ID, entered-vs-extracted comparison,
  confidence/recommendation, append-only run history). Deferred after confirming AI API cost is a
  non-issue at expected volumes (~$7-$170/year total, any model tier) - the real open question was
  whether to add a second paid AI vendor (Anthropic) alongside the existing OpenAI integration,
  not price. Pick up the proposal directly when this is prioritized.
