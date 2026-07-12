# Change: Semi-Automated PRC License Verification (AI/OCR-Assisted, Admin-Triggered)

## Status

**Deferred - Future Feature.** Not started; no target date. The proposal, spec deltas, and tasks
below are fully written and ready to pick up whenever this is prioritized - nothing here is
abandoned or rejected. Deferred after confirming cost isn't the blocker: at the stated volumes
(2,000 initial verifications, 500-1,000/year ongoing), Anthropic API cost across every model tier
comes out to roughly $7-$170/year total (Haiku ~$7 initial + ~$2-3/yr; Sonnet ~$26 initial +
~$6-13/yr; Opus ~$129 initial + ~$32-65/yr). The real open question was whether to introduce a
second paid AI vendor alongside this app's existing OpenAI integration (`IPromptExecutionService`)
at all, not price - revisit that question (and the other Open Decisions below) when this is
picked back up.

## Why

Today's PRC re-verification workflow (from `add-profile-data-continuity`) surfaces a member's PRC
License No. in an admin queue and lets an admin Approve or Reject, but the admin has to manually
compare the entered value against the uploaded PRC ID image with no assistance - error-prone and
slow at scale. This change adds an AI/OCR-assisted extraction-and-comparison step *inside* that
existing admin review, so the admin sees entered-vs-extracted side by side with a computed
confidence and recommendation before deciding - the human always makes the final call; the system
never auto-approves or auto-rejects.

## What Changes

- **No AI runs at submission.** The applicant enters Full Name, PRC License (Registration) No.,
  Registration Date, and Valid Until date, and uploads the PRC ID (existing JPG/PNG/PDF-under-2MB
  constraints) at both entry points - initial registration (wizard step 1) and post-approval PRC
  changes from My Profile. Submission succeeds immediately with standard field validation only;
  the application enters "Pending PRC Verification" via the existing queue.
- **Two new applicant fields**: `Registration Date` and `Valid Until` date join `PRC License No.`
  in both the wizard and the My Profile edit form - neither exists on `Member` today.
- **AI extraction is 100% admin-triggered, not automatic on submission.** An admin opens a
  member's PRC verification detail page (reached from the existing queue/notification) and
  explicitly runs extraction. The vision-capable LLM reads the uploaded ID and extracts Name,
  Registration No., Registration Date, Valid Until, License Status, and Profession, then the
  system compares each against the applicant-entered values (or, for Status/Profession, against
  expected constants) and computes per-field results, an overall confidence, and a recommendation.
  On extraction failure, the admin sees "Extraction unavailable - verify manually" and can still
  decide without it.
- **Every extraction attempt is persisted, never overwritten** - re-running verification appends a
  new `PrcVerificationRun` row (raw extracted values, per-field match results, image quality,
  overall confidence, recommendation, timestamp, provider/model, triggering admin).
- **A third admin action, "Request Additional Documents"**, joins Approve/Reject - for when the
  entered text is fine but the uploaded image itself needs to be redone. This needs a small new
  mechanic (`Member.PrcIdReuploadRequested`) since today's reupload gate only triggers when the
  License No. *text* changes - see Open Decision 4.
- **One shared pipeline for both entry points** - initial registration's first-ever PRC License
  No. and a later post-approval change both flow through the same admin queue, the same detail
  page, and the same AI extraction/comparison logic.

## Impact

- Affected specs: `prc-verification` (**new** capability), `member-profile` (**modified** -
  extends the PRC re-verification requirements from `add-profile-data-continuity`)
- Affected code (indicative - see `tasks.md` for the full breakdown):
  - `Domain`: `Member.PrcRegistrationDate`/`PrcValidUntilDate`/`PrcIdReuploadRequested`; new
    `PrcVerificationRun` entity; `PrcVerificationHistory` gains a `PrcVerificationRunId` link and a
    third `Decision` value
  - `Application`: new `IPrcIdExtractionService` (raw extraction only) + a separate, pure,
    unit-testable comparison/scoring component; new `PrcVerificationRunService`;
    `UpsertMyProfileAsync`'s reupload gate extended for `PrcIdReuploadRequested`
  - `Infrastructure`: `AnthropicPrcIdExtractionService` (new, second AI provider alongside the
    existing OpenAI-backed `IPromptExecutionService` - see Gap Analysis below); EF Core migration
  - `Web`: new PRC Verification detail/review page; wizard + My Profile gain the two new fields;
    member-facing banner extends to cover "additional documents requested"
- No changes to the existing Approve/Reject endpoints' shapes; this change adds a `run` step
  before them and a third `request-documents` action alongside them.

## Gap Analysis (found while writing this proposal)

- **Two new applicant fields don't exist yet**: Registration Date and Valid Until aren't on
  `Member` at all today - only `PrcLicenseNo` (text) exists.
- **"Full Name" needs no new field** - already composable from `FirstName`/`MiddleName`/
  `LastName`/`Suffix`, the same construction the wizard's step 4 review summary already does.
- **This repo already has one AI integration**: `Application.AI.IPromptExecutionService` /
  `Infrastructure.AI.OpenAiPromptExecutionService` (OpenAI-backed, config-driven, see
  `openspecs/ai-prompt-execution.md`). This change's Anthropic-backed extraction service is a
  **second, independent provider** living alongside it - no conflict, no shared code, just a new
  `Anthropic:ApiKey`/`Anthropic:Model` config pair and (most likely) a new NuGet dependency.
- **File access is already solved** - the extraction service reads the member's current PRC ID
  bytes via the existing `IFileStorageService`/`MemberUpload` plumbing; no new storage mechanism.
- **The admin queue already surfaces the right members** (`GetAllAsync`'s
  `pendingPrcVerificationOnly` filter, from the prior change) - this change adds a **new
  per-member detail page** as the primary way into that queue; see Open Decision 6 on whether
  today's inline quick Approve/Reject on the table stays alongside it.
- **"Request Additional Documents" needs new mechanics**: today's reupload gate
  (`UpsertMyProfileAsync`) only triggers when `PrcLicenseNo` *text* changes. If the text was
  correct and only the photo needs a redo, a member re-submitting the same unchanged text
  wouldn't trip the gate at all - the new file would silently overwrite with no new queue entry.
  See `Member.PrcIdReuploadRequested` and Open Decision 4.

## Open Decisions

1. **Auto-run extraction on page open, or an explicit "Run AI Verification" button?**
   Recommended: **explicit button**. Matches "admin-triggered" in the feature's own framing,
   avoids a surprise API call (cost + latency) on every page visit, and gives the admin a moment
   to look at the entered values/document before spending an API call.
2. **Confidence weighting formula and recommendation-tier thresholds** - needs your business
   judgment. Recommended starting default:
   - **Approve** if overall confidence ≥ 90% AND Registration No. matches exactly AND Valid Until
     is a future date AND License Status reads REGISTERED.
   - **Manual Review** if overall confidence is 70-89% (or any independent check is borderline).
   - **Reject** otherwise - overall confidence < 70%, OR Registration No. doesn't match, OR the
     license is expired, OR status isn't REGISTERED (these three are **hard gates**, not just
     inputs to the weighted average - a mismatched Registration No. should never average out to a
     recommend-approve just because the name and dates look fine).
   - Overall confidence itself: a weighted average - Registration No. heaviest (primary
     identifier), then Name, then Registration Date/Valid Until/License Status/image quality.
     Exact weights are open for your input; recommend documenting them as named constants (not
     magic numbers) wherever they land so they're easy to revisit later.
3. **Expected Profession value**: hardcode `"Master Plumber"`, or make it a `SystemConfig` value
   (same pattern already used for `MembershipGracePeriodDays`) so it's changeable without a
   redeploy if PSMPE ever accredits another profession? Recommended: **`SystemConfig`**, for
   consistency with the existing pattern and because it's a one-line addition either way.
4. **"Request Additional Documents" mechanics**: confirms the `Member.PrcIdReuploadRequested` flag
   design in the Gap Analysis above - the reupload gate becomes `prcLicenseNoChanged OR
   PrcIdReuploadRequested`, and a validated save clears the flag. Flagging the *mechanism* as a
   decision to confirm, not just documenting that a flag will exist.
5. **Registration Date / Valid Until - required to submit, or optional?** Like `FirstName`/
   `LastName`/`Chapter`/`MemberType` (blocks submission if empty), or optional like `Address`/
   `Gender` (today's `PrcLicenseNo` is itself optional-to-submit)? Recommended: **optional**,
   consistent with how `PrcLicenseNo` is already treated.
6. **Keep today's inline quick Approve/Reject on the PRC Verifications table, or route everything
   through the new detail page?** Recommended: **route everything through the detail page** -
   keeping a no-AI-glance-required fast path undermines the point of building the review screen.
   The detail page still lets an admin approve without running AI at all if they choose to (the
   extraction-failure fallback already has to support deciding without AI, so "admin skips AI on
   purpose" is the same code path, not extra work).
7. **Should Registration Date and Valid Until stage/gate together with PRC License No., or change
   independently?** All three now describe the same physical PRC ID card, so a change to *any* of
   them arguably ought to require the same fresh-reupload-and-re-review gate `PrcLicenseNo` alone
   uses today - otherwise a member could quietly edit just the Valid Until date with no reupload
   and no admin visibility at all, since nothing would gate on it. Recommended: **broaden the
   existing gate to `PrcLicenseNo` OR `PrcRegistrationDate` OR `PrcValidUntilDate` changing**, and
   stage all three together (extend `PendingPrcLicenseNo`'s pattern to
   `PendingPrcRegistrationDate`/`PendingPrcValidUntilDate`, all committed or discarded as one unit
   on Approve/Reject) - keeps the "one PRC card = one pending change" mental model intact rather
   than letting the three fields drift out of sync with each other.
