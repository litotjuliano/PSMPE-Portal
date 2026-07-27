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
  - Query: `page`, `pageSize`, `sortBy` (`lastName` | `membershipNo` | `chapter` | `status`), `sortDir`,
    `status` (optional `MembershipStatus` filter), `pendingApprovalOnly` (optional bool — `true`
    returns only members with `ApprovedAt == null`; see the Draft/Approval/Status split below for
    why this is a separate filter from `status`)
  - **Always excludes drafts** (`SubmittedAt == null`) regardless of other filters — an
    in-progress, not-yet-submitted application is invisible here, not just unapproved.
  - Response: `PagedResult<MemberDto>`
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
  - Request: `{ userId, membershipNo, firstName, middleName, lastName, suffix, birthdate, gender, civilStatus, educationLevel, schoolName, courseYearGraduated, specifiedProfession, mobileNumber, houseNo, street, barangay, cityMunicipality, province, zipCode, mailingHouseNo, mailingStreet, mailingBarangay, mailingCityMunicipality, mailingProvince, mailingZipCode, prcLicenseNo, prcRegistrationDate, prcValidUntilDate, ptrNumber, tin, chapter, company, memberType, renewalDueDate, nationalDuesReferenceNo }`
    (see "Membership Application Form fields" below for what each new field maps to on the paper
    form, and the "RMP" naming note)
  - Does **not** create a login account — that's `POST /api/auth/register`'s job. `400` if
    `userId` doesn't exist; `409` if that user already has a profile or `membershipNo` collides.
  - Always starts as `Status: Pending`, `ApprovedAt: null`, `SubmittedAt: now` — admin-entered
    profiles skip the draft phase entirely, since they're complete the moment they're created.
- `PUT /api/members/{id}` — admin edit, including `Status` and `MemberType`
  - Auth: `members:manage` permission
- `PUT /api/members/me` — self-service edit/autosave of the caller's own profile
  - Auth: authenticated (any role)
  - Request: same shape as create, minus `userId`/`membershipNo`/`Status` — those are
    business-controlled, not self-service (`MemberType` *is* self-service — it's chosen at
    registration, not a business decision like `Status`)
  - **Upserts**: creates the profile (with a server-generated `MembershipNo`, `Status: Pending`,
    `SubmittedAt: null`) if the caller doesn't have one yet. This is the wizard's per-step autosave
    mechanism — every "Save & Continue" click calls this with whatever's been filled in so far, so
    closing the browser mid-wizard and returning later resumes with everything intact. Does *not*
    set `SubmittedAt` — that's a separate, explicit action (below).
- `POST /api/members/me/submit` — self-service: finalizes the draft into a submitted application
  - Auth: authenticated (any role)
  - `404` if the caller has no draft at all (hasn't saved anything yet); `400` if
    `FirstName`/`LastName`/`Chapter`/`MemberType` are still empty (lists what's missing);
    otherwise sets `SubmittedAt` to now if not already set (idempotent).
  - This is what makes the application visible to admins at all — see Draft/Approval/Status below.
- `POST /api/members/{id}/approve` — admin marks an application as reviewed
  - Auth: `members:manage` permission
  - Sets `ApprovedAt` to now if not already set; idempotent (approving twice is a no-op success).
  - Does **not** change `Status` — approval and payment are independent gates, see below.
- `DELETE /api/members/{id}` — admin removes just the member profile
  - Auth: `members:manage` permission
  - Leaves the underlying login/role account intact — retiring someone's membership record
    doesn't delete their system account.

## Draft vs. Submitted vs. Approved vs. Status

Four separate axes on the same row, easy to conflate:

- **`SubmittedAt`** — has the applicant finished the wizard? `null` while the wizard's per-step
  autosave (`PUT /me`) has created a row but the applicant hasn't reached the final "Submit"
  step (`POST /me/submit`) yet. A draft is invisible to every admin-facing query
  (`GET /api/members`, Membership Approvals, notifications) — it isn't a member yet, just a
  half-filled form.
- **`ApprovedAt`** — has an admin reviewed a *submitted* application? `null` until a
  `POST /api/members/{id}/approve` call. Independent of `Status`.
- **`MemberType`** (`Domain.Enums.MemberTypes`, const list like `Chapters`) — a category chosen at
  registration (currently only `"Regular Member"`). Purely descriptive, not a workflow state.
- **`Status`** (`MembershipStatus`: `Pending`/`Active`/`Expired`/`Deactivated`) — payment-gated.
  Per the business rule: approved members who pay dues become `Active`; approved-but-unpaid
  members *stay* `Pending`. No Payments/Dues domain exists yet, so today an admin flips `Status`
  to `Active` manually via `PUT /api/members/{id}` once dues are confirmed paid out of band.

Because an approved-but-unpaid application still has `Status: Pending`, the Membership Approvals
queue and the notification bell filter on `pendingApprovalOnly=true` (`ApprovedAt == null`), not
`status=Pending` — otherwise already-approved members would never disappear from the "needs
review" list. And because `GetAllAsync` unconditionally excludes drafts, an in-progress
application never shows up there either, regardless of which filters are passed.

## Grace period

`MemberDto.IsInGracePeriod` is computed (not stored): `true` when `Status == Active`,
`RenewalDueDate` has passed, and today is still within `RenewalDueDate + MembershipGracePeriodDays`
(a `SystemConfig` row, default `30`, read by `MemberService` with an in-code fallback if unseeded).
Gives lapsed-but-recent members a window of continued access rather than an immediate cutoff.
Nothing currently enforces reduced access during grace (no Certificates/CPD/Events exist yet to
gate) — it's exposed as a flag for the frontend to act on when those features exist.

## Registration: simple sign-up now, resumable application wizard later

Sign-up and the membership application are two separate, decoupled flows:

- **`RegisterPage`** (`/register`, public) is a plain one-step form — Email, Password, Confirm
  Password, Display Name, optional Username. Calls `POST /api/auth/register` only, then redirects
  to the dashboard (`/`), logged in. No Member profile is created at this point.
- **`MyProfilePage`** (`/profile`, authenticated) hosts the actual 4-step application wizard from
  the product spec (Personal Info → Contact Info → Additional Info → Payment Details), via
  `MembershipApplicationWizardCard`, whenever the caller has no Member profile yet or has one with
  `submittedAt: null` (still a draft) — once `submittedAt` is set, this page instead shows the
  ordinary flat "My Profile" edit form (`MyProfileCard`) as before.
  - **Autosave/resume**: every "Save & Continue" click calls `PUT /api/members/me` with whatever's
    been filled in so far, then advances the step — so leaving mid-wizard and coming back later
    resumes with that data intact. Resume position is derived, not stored (see
    `furthestStepReached`/`hasCompleted*Info` in `MyProfilePage.tsx`): resumes at the first step
    whose own required fields aren't all filled yet - Personal Info (name/chapter/member type/etc.),
    then Contact Info (mobile number/residence address), then Additional Info (PTR Number) - Payment
    Details has no required Member field of its own (Proof of Payment is an upload, and the
    terms/consent checkboxes are never persisted), so it's never a distinct resume gate.
  - **Additional Information step** (3rd) collects PTR Number, TIN (optional), and Company
    (optional) - alongside a read-only display of the caller's account email.
  - **Final step** ("Payment Details") shows the membership fee/payment instructions, collects the
    Proof of Payment upload, then a review summary + terms checkbox; Submit calls
    `POST /api/members/me/submit`, which is what actually makes the application visible to admins.
  - `DashboardPage` shows a "Complete your membership application" banner (checks
    `GET /api/members/me`, shown whenever there's no profile yet or `submittedAt` is still null)
    linking to `/profile` — this is what surfaces the resumable wizard from the dashboard.
- Map-based address picker shown in the mockup is not collected — no Maps API integration exists
  (see Open questions); the residence/mailing address is instead collected as plain structured
  text fields (see "Membership Application Form fields" below). Photo and RMP ID are collected in
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
  tables, PRC Verifications queue, notifications), but every internal identifier (`PrcLicenseNo`,
  `PrcIdVerified`, `PendingPrcLicenseNo`, `PrcVerificationRejectedReason`, the `/prc-verifications`
  route, `UploadKind.PrcId`) deliberately keeps its original name — renaming the actual
  properties/columns/endpoints would be a large, purely-cosmetic ripple with no functional benefit.
- **Residence address**: `HouseNo` (optional), `Street`, `Barangay`, `CityMunicipality`, `Province`,
  `ZipCode` — replaces the old single `Address` string.
- **Mailing address**: `MailingHouseNo`, `MailingStreet`, `MailingBarangay`,
  `MailingCityMunicipality`, `MailingProvince`, `MailingZipCode` — new. The wizard/profile offer a
  client-side-only "Same as Residence Address" checkbox that copies the residence values across at
  save time; there's no stored flag, just six independent columns.
- **Education**: `EducationLevel` ("Technical School" / "College / University"), `SchoolName`,
  `CourseYearGraduated` (free text, e.g. "BSCE 2023").
- **Profession**: `SpecifiedProfession` ("Master Plumber" / "Other Professional Related").
- **RMP license dates**: `PrcRegistrationDate`, `PrcValidUntilDate` (plain `DateOnly?` data entry —
  not the deferred AI/OCR extraction proposal). Staged the same way `PrcLicenseNo` already was:
  changing the license number **or** either date requires a fresh RMP ID re-upload, and stages all
  three together as `PendingPrcLicenseNo`/`PendingPrcRegistrationDate`/`PendingPrcValidUntilDate`
  until an admin approves or rejects via the existing PRC Verifications queue.
- **Age** is derived from `Birthdate` for display only — never persisted.
- **Data Privacy Consent**: a second wizard checkbox (RA 10173 text + `privacy.gov.ph` link)
  alongside the existing membership-terms checkbox — same pre-existing pattern as that checkbox
  (client-side Submit gate only, not persisted to the `Member` record).

## File uploads (photo, RMP ID, proof of payment, etc.)

Files are **not** stored in Postgres, and are **not** served as plain static URLs - both were
deliberate calls, not the obvious defaults:

- **`MemberUpload`** (`Domain.Entities`) is a thin pointer row - `UserId`, `Kind`
  (`Photo`/`PrcId`/`ValidGovernmentId`/`Signature`/`ProofOfPayment`), `StorageKey`,
  `ContentType` - a few dozen bytes, regardless of the file's actual size. Keyed by `UserId`, not
  `MemberId`, so a photo/RMP ID can be uploaded before any
  `Member` row exists yet (before Personal Info is saved). One row per `(UserId, Kind)` - a
  re-upload overwrites the pointer (and the file at the same storage key), no accumulation.
  Storing the bytes directly in Postgres was considered and rejected: DigitalOcean prices managed
  Postgres storage far more expensively than object storage, and it only gets worse as photos +
  PRC IDs + eventual CPD certificates accumulate across the whole membership base - that would
  force costly compute-tier upgrades just for storage headroom.
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
- `GET /api/members/{id}/{kind}` (same kinds) - `members:view` permission.

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

## Membership Approvals + notifications

`MembershipApprovalsPage` (`/membership-approvals`, Admin/Super Admin) lists members with
`pendingApprovalOnly=true` and lets an admin Approve inline, or click through to `/members/{id}`
(`MemberFormCard` also has an inline Approve button there, since that's where a notification click
lands - that page also opens read-only, "Approval"-titled, per the same review-not-edit reasoning
as the applicant-facing flow below). The topbar notification bell and the dedicated
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

## Open questions / TODO

- **`IFileStorageService` only has a local-disk implementation** (see "File uploads" above) -
  won't survive a redeploy/restart in production. A `DigitalOceanSpacesFileStorageService` is the
  intended next implementation once real Spaces credentials exist; the interface seam makes that
  a contained addition, not a rewrite.
- **Chapter is a fixed const list** (`Domain.Enums.Chapters`, mirrors `RoleNames`/`Permissions`'s
  style), not a database-editable entity — no mockup or requirement showed chapter CRUD.
  Revisit as a real entity+table if admins ever need to add/rename chapters without a deploy.
- **Payments/Dues domain doesn't exist yet**: `Status` transitions to `Active` are entirely manual
  (an admin edits the record once dues are confirmed paid out of band). Once a Payments domain
  exists, it should be the thing that flips `Status`, not manual admin edits.
- **`MembershipNo` auto-generation is not perfectly race-safe under concurrency**:
  `GenerateMembershipNoAsync` generates a sequential zero-padded number one past the *highest*
  existing `MembershipNo` (not a row count - a row-count-based scheme deterministically collides,
  on every subsequent attempt, as soon as any Member row is ever deleted and leaves a gap below the
  count). The unique index still guarantees no duplicate is ever persisted, but a genuine
  concurrent collision (two first-time registrations landing in the same instant) would surface as
  a `SaveChanges` failure rather than a graceful retry — acceptable at current scale, revisit if
  registration volume grows.
- No audit log for profile/status changes yet (same gap noted for role changes in `roles.md`).
- **Semi-automated, AI/OCR-assisted PRC License verification is a deferred future feature** - a
  full OpenSpec proposal already exists at `openspec/changes/add-prc-ai-verification/` (admin-
  triggered vision-LLM extraction from the uploaded PRC ID, entered-vs-extracted comparison,
  confidence/recommendation, append-only run history). Deferred after confirming AI API cost is a
  non-issue at expected volumes (~$7-$170/year total, any model tier) - the real open question was
  whether to add a second paid AI vendor (Anthropic) alongside the existing OpenAI integration,
  not price. Pick up the proposal directly when this is prioritized.
