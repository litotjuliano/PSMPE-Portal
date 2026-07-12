# Tasks: add-prc-ai-verification

## 0. Resolve Open Decisions First

- [ ] 0.1 Confirm auto-run vs. explicit "Run AI Verification" button (proposal.md Decision 1)
- [ ] 0.2 Confirm confidence weighting formula and recommendation-tier thresholds (Decision 2)
- [ ] 0.3 Confirm expected Profession value: hardcoded vs. `SystemConfig` (Decision 3)
- [ ] 0.4 Confirm `PrcIdReuploadRequested` mechanics for Request Additional Documents (Decision 4)
- [ ] 0.5 Confirm Registration Date/Valid Until required-to-submit or optional (Decision 5)
- [ ] 0.6 Confirm whether the PRC Verifications table keeps inline quick Approve/Reject or routes
      everything through the new detail page (Decision 6)
- [ ] 0.7 Confirm Registration Date/Valid Until stage/gate together with PRC License No. as one
      unit (Decision 7)

## 1. Domain & Data

- [ ] 1.1 Add `Member.PrcRegistrationDate` (`DateOnly?`), `Member.PrcValidUntilDate` (`DateOnly?`)
- [ ] 1.2 Add `Member.PendingPrcRegistrationDate` (`DateOnly?`), `Member.PendingPrcValidUntilDate`
      (`DateOnly?`) - extends the existing `PendingPrcLicenseNo` pattern to all three PRC fields
      (pending Decision 7)
- [ ] 1.3 Add `Member.PrcIdReuploadRequested` (`bool`)
- [ ] 1.4 New `PrcVerificationRun` entity: member, triggering admin, provider, model, extracted
      raw fields (name, registration no., registration date, valid until, license status,
      profession), per-field match results, image quality, overall confidence, recommendation,
      extraction-succeeded flag + failure reason, document storage key snapshot
- [ ] 1.5 New enums: `PrcImageQuality { Good, Fair, Poor }`,
      `PrcVerificationRecommendation { Approve, ManualReview, Reject }`
- [ ] 1.6 `PrcVerificationHistory` gains nullable `PrcVerificationRunId` (FK-less reference, same
      pattern as its existing `MemberId`) and its `Decision` enum gains `MoreDocumentsRequested`
- [ ] 1.7 If Decision 3 picks `SystemConfig`: seed an `ExpectedPrcProfession` config row (default
      `"Master Plumber"`)
- [ ] 1.8 EF Core migration for all of the above

## 2. Application Layer - Extraction & Comparison

- [ ] 2.1 New `IPrcIdExtractionService` (`Application.Members`) - `ExtractAsync(Stream content,
      string contentType, CancellationToken) -> PrcIdExtractionResultDto` (raw extraction only,
      no comparison)
- [ ] 2.2 New `AnthropicPrcIdExtractionService` (`Infrastructure.AI`) - vision-capable Anthropic
      model call; new `Anthropic:ApiKey`/`Anthropic:Model` config (`.env.example`,
      `appsettings.json`), new NuGet dependency
- [ ] 2.3 New pure comparison/scoring component (no AI dependency, fully unit-testable): name
      fuzzy match (normalize + similarity %), registration no. exact match (normalize + 100/0),
      date parsing + exact match for both dates, independent expiry check, independent license
      status check, profession check against expected value
- [ ] 2.4 Overall confidence + recommendation calculation per Decision 2's resolved thresholds,
      with Registration No. mismatch / expired / non-REGISTERED status as hard gates against
      Approve regardless of other scores
- [ ] 2.5 Unit tests for the comparison/scoring component covering every rule above, entirely
      without a real AI call (feed it synthetic extracted-vs-entered value pairs)

## 3. Application Layer - Verification Runs & Decisions

- [ ] 3.1 New `PrcVerificationRunService`: `RunVerificationAsync(memberId, adminUserId,
      cancellationToken)` (loads member + current PRC ID file via existing
      `IFileStorageService`/`MemberUpload`, calls extraction, runs comparison, persists a new
      `PrcVerificationRun`, returns it - never overwrites a prior run); `GetRunsAsync(memberId)`
- [ ] 3.2 Extraction failure path: `RunVerificationAsync` still persists a
      `PrcVerificationRun` with `ExtractionSucceeded = false` and a failure reason, and returns a
      result the frontend can render as "Extraction unavailable - verify manually"
- [ ] 3.3 Extend `MemberService.UpsertMyProfileAsync`'s reupload gate: require reupload if
      `PrcLicenseNo`/`PrcRegistrationDate`/`PrcValidUntilDate` changed (per Decision 7) OR
      `PrcIdReuploadRequested` is set; on a validated save, stage all changed PRC fields as
      pending together and clear `PrcIdReuploadRequested`
- [ ] 3.4 Extend `ApprovePrcVerificationAsync` to commit all three pending PRC fields together
      (not just `PendingPrcLicenseNo`)
- [ ] 3.5 New `RequestAdditionalPrcDocumentsAsync(memberId, reason, adminUserId)`: sets
      `PrcIdReuploadRequested = true`, clears any pending PRC fields, sets
      `PrcVerificationRejectedReason` (reused field - distinguished from a real rejection by the
      `Decision` value in the audit trail), records a `MoreDocumentsRequested`
      `PrcVerificationHistory` entry
- [ ] 3.6 `MemberDto` gains `PrcRegistrationDate`, `PrcValidUntilDate`,
      `PendingPrcRegistrationDate`, `PendingPrcValidUntilDate`, `PrcIdReuploadRequested`
- [ ] 3.7 Unit tests: gate extension (any of the three fields, or `PrcIdReuploadRequested`, trips
      the reupload requirement); Approve commits all three pending fields; Request Additional
      Documents sets the flag and clears pending fields without touching current values; a
      reupload-requested member's next save succeeds without changing PRC field text

## 4. Web API

- [ ] 4.1 `POST /api/members/{id}/prc-verification/run` - triggers extraction/comparison, returns
      the new `PrcVerificationRun`, `members:manage`
- [ ] 4.2 `GET /api/members/{id}/prc-verification/runs` - run history for the detail page,
      `members:manage` (or `members:view` - confirm alongside Decision 6)
- [ ] 4.3 `POST /api/members/{id}/prc-verification/request-documents` - body `{ reason }`,
      `members:manage`
- [ ] 4.4 Integration tests: run endpoint persists a run (mock/stub the extraction service for
      deterministic tests, do not call a real LLM in CI); extraction-failure path still returns a
      run with `ExtractionSucceeded = false`; request-documents endpoint sets the flag correctly

## 5. Web (Admin UI)

- [ ] 5.1 New `PrcVerificationDetailPage.tsx` (`/prc-verifications/:id`): entered values + PRC ID
      document view (reuses the existing authenticated blob-fetch pattern); "Run AI Verification"
      action (or auto-run, per Decision 1) with a loading state; entered-vs-extracted side-by-side
      results, per-field match, image quality, overall confidence, recommendation once a run
      exists; "Extraction unavailable - verify manually" fallback on failure
- [ ] 5.2 Approve / Reject (reason) / Request Additional Documents (reason) actions on the detail
      page
- [ ] 5.3 `PrcVerificationsTable.tsx`: row action becomes "Review" linking to the detail page (per
      Decision 6 - remove or keep the existing inline quick actions)
- [ ] 5.4 Router entry `/prc-verifications/:id`, nav/notification links updated to point at it

## 6. Web (Member-Facing)

- [ ] 6.1 Add Registration Date and Valid Until inputs to wizard step 1
      (`MembershipApplicationWizardCard.tsx`), next to PRC License No. and the PRC ID upload
- [ ] 6.2 Add the same two fields to `PersonalInformationSection.tsx`'s Edit Mode, View Mode, and
      the pending-value note (extended for all three PRC fields, not just PRC License No.)
- [ ] 6.3 Extend the existing rejection banner to also render a distinct "please re-upload your
      PRC ID" message when `PrcIdReuploadRequested` is set, separate from a rejection

## 7. Verification

- [ ] 7.1 E2E: submit with Registration Date/Valid Until/PRC ID → admin opens detail page →
      Run AI Verification → result renders correctly against a known sample PRC ID
- [ ] 7.2 E2E: extraction failure (e.g. temporarily invalid API key) still allows Approve/
      Reject/Request Additional Documents
- [ ] 7.3 E2E: Approve commits all three pending PRC fields together; Reject leaves current values
      standing; Request Additional Documents lets a member re-upload without retyping unchanged
      text and triggers a fresh review
- [ ] 7.4 E2E: re-running verification for the same member appends a second `PrcVerificationRun`
      rather than overwriting the first
- [ ] 7.5 `dotnet build`/`dotnet test`, `tsc`/`lint`/`build` all clean
