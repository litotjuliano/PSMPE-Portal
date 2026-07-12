# Tasks: add-profile-data-continuity

## 1. Domain & Data

- [x] 1.1 Add `PrcIdVerified` (bool) to the `Member` entity
- [x] 1.2 EF Core migration for `PrcIdVerified`
- [x] 1.3 Confirm approval handler only changes status (no data copy) — already true, no code change
- [x] 1.4 Add `PendingPrcLicenseNo` (string?) and `PrcVerificationRejectedReason` (string?) to `Member`
- [x] 1.5 New `PrcVerificationHistory` entity (audit trail: old/new value, document reference, decision, reason, deciding admin) + EF Core migration

## 2. Application Layer

- [x] 2.1 Add generic `Result<T>` to `Common/Models` (needed so self-service profile updates can fail validation)
- [x] 2.2 `UpdateMyProfileRequest` gains `PrcIdReuploaded`; `MemberDto` gains `PrcIdVerified`/`PendingPrcLicenseNo`/`PrcVerificationRejectedReason`. `UpdateMemberRequest` does NOT gain `PrcIdVerified` (superseded by 2.7's dedicated Approve/Reject - no raw admin toggle)
- [x] 2.3 `UpsertMyProfileAsync`: once `SubmittedAt` is set, reject Member Type/Chapter changes; while still a draft, leave them freely editable (matches the wizard's existing behavior)
- [x] 2.4 `UpsertMyProfileAsync`: once `SubmittedAt` is set, require a fresh PRC ID re-upload (verified via `MemberUpload` timestamp, not just a client-asserted flag) whenever `PrcLicenseNo` changes; stage the new value as `PendingPrcLicenseNo` (NOT an immediate overwrite) and clear any prior `PrcVerificationRejectedReason`; leave everything untouched when `PrcLicenseNo` is unchanged
- [x] 2.5 `GetAllAsync` gains a `pendingPrcVerificationOnly` filter: `PendingPrcLicenseNo != null` OR (`!PrcIdVerified` AND `PrcLicenseNo != null` AND `PendingPrcLicenseNo == null`) — covers both a proposed change and a never-reviewed first submission
- [x] 2.6 New `ApprovePrcVerificationAsync`/`RejectPrcVerificationAsync`: copy/discard the pending value, set `PrcIdVerified`/`PrcVerificationRejectedReason` accordingly, record a `PrcVerificationHistory` row
- [x] 2.7 Two new endpoints (`POST /api/members/{id}/prc-verification/approve`, `/reject`), admin-only (`members:manage`)
- [x] 2.8 Unit tests: draft-phase vs. post-submit Member Type/Chapter behavior; PRC re-upload gating (missing flag, no upload row, stale upload, fresh upload stages pending without touching current/verified); unchanged-PRC-license no-op; Approve (with/without a pending change) and Reject (with/without a pending change) behavior; queue filter includes never-reviewed and pending-change members, excludes verified ones; rejecting a never-reviewed member keeps them in the queue

## 3. Web (UI)

- [x] 3.1 Add PRC License No. to wizard step 1 (Personal Information), next to the PRC ID upload
- [x] 3.2 Add Company as a real field to wizard step 4 (Additional Information), replacing today's review-only screen
- [x] 3.3 Replace the single always-editable profile form with a 4-tab profile (Personal/Contact/Account/Additional Information), used once `SubmittedAt` is set (same boundary as today's wizard-vs-profile switch)
- [x] 3.4 Each tab has its own View Mode (default) / Edit Mode toggle and Save action, independent of the other tabs
- [x] 3.5 Protected fields (Membership No., Status, Email, Member Type, Chapter) render read-only in every tab regardless of edit state
- [x] 3.6 Personal Information tab: photo display/re-upload, PRC ID view/re-upload wired to the re-verification rule (block Save with inline guidance if PRC License No. changed without a re-upload this session); shows a "pending admin verification" note and a rejection-reason banner when applicable
- [x] 3.7 Account Information tab has no Edit action (nothing self-service-editable there)
- [x] 3.8 Remove the raw "PRC ID Verified" checkbox from the admin Members edit form (superseded by 3.9)
- [x] 3.9 New admin **PRC Verifications** page/table (mirrors Membership Approvals): lists the queue, Approve/Reject (reason required) actions, a "View ID" link to the PRC ID document; nav entry + route, gated Admin/Super Admin
- [x] 3.10 Topbar notification bell: second section/count for pending PRC verifications alongside the existing membership-application count

## 4. Verification

- [x] 4.1 E2E: register → wizard (including PRC License No. and Company) → submit → tabbed profile shows all submitted values
- [x] 4.2 E2E: edit a tab + save → that tab's View Mode and the admin member record both updated
- [x] 4.3 Negative test: crafted request attempting to change Email/Member Type/Chapter is rejected
- [x] 4.4 E2E: change PRC License No. without re-upload is rejected; with re-upload stages a pending value (current value unchanged) → admin Approves (value commits, verified) or Rejects (pending discarded, reason shown to member) → both recorded in the audit trail
