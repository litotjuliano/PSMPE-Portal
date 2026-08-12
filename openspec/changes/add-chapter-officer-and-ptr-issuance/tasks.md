# Tasks: add-chapter-officer-and-ptr-issuance

**Goal:** Record an optional chapter officer post (year + role) and the PTR's place/date of issue,
and expand Membership Type from one option to four — across the registration wizard, the profile
tabs, and the admin member form.

**Architecture:** Four new nullable columns on `Member`, threaded through the four positional DTO
records and all three write paths in `MemberService`. Membership Type needs no schema work at all —
the column is unvalidated free text, so the three new options are a constants-and-dropdown change.
A new `core/utils/memberFields.ts` holds the shared `isValidChapterYear` check so all three forms
enforce the same range.

**Tech Stack:** .NET 8 + EF Core + Postgres (backend), React 19 + Vite + TypeScript (frontend).
No test runner exists in `apps/web`; the backend has xUnit unit and integration projects.

**Before starting:** read `proposal.md` in this folder.

---

## 1. Schema and domain

- [x] Add `ChapterYear` (`int?`) and `ChapterPosition` (`string?`) to `Member.cs`, grouped with
      `Chapter`/`MemberType`. Document why the name is `Chapter*` and not `Position`.
- [x] Add `PtrPlaceIssued` (`string?`) and `PtrDateIssued` (`DateOnly?`) immediately after
      `PtrNumber`.
- [x] Declare `HasMaxLength(128)` for both new strings in `MemberConfiguration.cs`.
- [x] Add `New`, `Renewal`, `SeniorCitizen` to `MemberTypes.cs`, keeping `Regular` first, with a
      comment explaining why it stays.
- [x] Generate `AddChapterOfficerAndPtrIssuance` — verify it is exactly four `AddColumn`s with no
      backfill and no unrelated drift.

## 2. Application layer

- [x] Add the four fields to all four records in `MemberDto.cs`.
- [x] Extend the `ToDto` projection in `MemberService.cs`.
- [x] Assign all four in `CreateAsync`, `UpdateAsync`, and `UpsertMyProfileAsync`.
- [x] Add `chapterPosition` and `ptrPlaceIssued` to `ValidateMemberFieldLengths`' existing
      `(value, label, maxLength)` tuple loop rather than as new `if` lines.
- [x] Add the `ChapterYear` 1900–2200 guard beside the `YearsOfPractice` one in
      `UpsertMyProfileAsync`, with a comment on why it's a sanity check rather than a crash guard.
- [x] Confirm none of the four joins the `missing` list in `SubmitMyProfileAsync` — all optional.
- [x] Confirm the officer pair is assigned *outside* the `!isDraft` Chapter/MemberType lock.
- [x] Confirm `hasProfessionalInfo` is untouched, so completeness percentages don't shift.

## 3. Frontend plumbing

- [x] Add the four fields to `Member` in `core/types/member.ts` and to all three request shapes in
      `memberApi.ts`.
- [x] Add the three new values to the `MemberTypes` const, mirroring the backend order.
- [x] Extend `buildFullProfileRequest` in `profile-sections/shared.ts`.
- [x] Create `core/utils/memberFields.ts` exporting `CHAPTER_YEAR_MIN`/`MAX`,
      `CHAPTER_YEAR_ERROR`, and `isValidChapterYear`.

## 4. Form surfaces

- [x] **Wizard** — state fields, `WizardFieldErrors.chapterYear`, the officer pair after the Chapter
      select in step 1, the PTR pair beside PTR Number in step 2, and the range check in
      `validateStep`'s `step === 0` branch.
- [x] **`MyProfilePage`** — blank initial wizard state, hydration from an existing member, and the
      draft-save payload (`Number(...)` conversion for the year, mirroring `yearsOfPractice`).
- [x] **`PersonalInformationSection`** — form state, the pre-save range check, the save payload, and
      a new editable "Chapter Officer" group under the read-only Membership group. Update the
      component's doc comment, which claimed the group was entirely read-only.
- [x] **`ProfessionalLicensingSection`** — form state, save payload, and the PTR pair in both the
      edit and view branches.
- [x] **`MemberFormCard`** + **`MemberFormPage`** — state, hydration, both payloads (create and
      update), `ViewField` entries, and edit inputs.

## 5. Tests

- [x] Thread the four new arguments through every request literal in `MemberServiceTests.cs`,
      `AdminControllerTests.cs`, and `MembersControllerTests.cs`. Round-trip literals that copy from
      a DTO must copy the new fields too, so the assertions actually prove they survive.
- [x] Add optional parameters for the four fields to `BuildRequest`.
- [x] `ChapterYear` of 1899 and 2201 are both rejected.
- [x] All four round-trip through `UpsertMyProfileAsync` intact.
- [x] An over-length `ChapterPosition` (129 chars) is rejected.
- [x] The officer pair is still editable after `SubmittedAt` is set — pins the deliberate exclusion
      from the Chapter/MemberType lock.
- [x] Existing submit tests still pass unchanged, proving the four fields don't gate submission.

## 6. Docs

- [x] `openspecs/members.md` — membership-type list with the New/Renewal vs Senior Citizen
      limitation, the chapter officer pair, PTR issuance, and the updated Additional Information
      step description.
- [x] This change package.

## 7. Verification

- [x] `dotnet build src/PSMPE.Portal.sln` — 0 warnings, 0 errors. **Stop the dev API first**; it
      holds locks on the output DLLs and the build fails with MSB3027 otherwise.
- [x] `dotnet test src/PSMPE.Portal.sln --no-build` — 291 passing, 0 failing.
- [x] `npx tsc -b --noEmit` and `npm run lint` in `apps/web` — 0 errors, only the 3 known
      pre-existing warnings (`ApexChart`, `useLayoutContext` ×2).
- [x] `npm --prefix apps/web run build`.

### Not yet done — needs a running app and a browser

- [ ] Apply the migration (happens automatically on the next API start via `Program.cs`'s
      `MigrateAsync`).
- [ ] Register a fresh draft, fill Year/Position for a **non-NCR** chapter, and confirm the fields
      appear — the original request said "in NCR Chapter", so this is the check that the
      all-chapters decision actually shipped.
- [ ] Leave all four new fields blank and confirm submit still succeeds.
- [ ] Confirm each value survives a round-trip through the profile tab and the admin member form.
- [ ] Enter `1899` in Chapter Year and confirm both the client and the server reject it.
- [ ] Select each of the three new Membership Types; confirm the value reaches the database and
      prints on the generated receipt.
- [ ] Open an existing `"Regular Member"` record in the admin form and confirm the dropdown still
      shows that value rather than falling back to blank.
