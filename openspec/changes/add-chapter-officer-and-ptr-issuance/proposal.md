# Change: Chapter Officer Details, PTR Issuance Details, and Expanded Membership Type

## Status

**Implemented.** Design approved 2026-08-08 through collaborative brainstorming, with four
scope questions answered directly by the user (see "Decisions" below). Codebase researched
first-hand — the `Position` collision, the absent server-side whitelist, and the existing
`"Regular Member"` row counts were all verified against the running database rather than assumed.
Built and verified the same day: backend build clean, 291 tests passing, frontend
typecheck/lint/build clean. See `tasks.md` for what remains unverified (the live browser pass).

## Why

Three gaps between the portal's application form and the official PSMPE paper form:

1. **Chapter service isn't recorded.** The form captures *which* chapter a member belongs to
   (`Member.Chapter`) but nothing about any officer post they hold in it.
2. **PTR provenance is missing.** `Member.PtrNumber` is collected alone. A Professional Tax Receipt
   also carries a place and date of issue; without them there's no way to tell whether a stored PTR
   is current or where it came from.
3. **Membership Type is a one-item dropdown.** `MemberTypes.All` contained exactly one value,
   `"Regular Member"`, making the select a control with nothing to select. The paper form
   distinguishes New from Renewal, and treats Senior Citizen as its own category.

## What Changes

**Four new nullable columns on `Member`:**

| Column | Type | Max | Purpose |
|---|---|---|---|
| `ChapterYear` | `int?` | — | Year the officer post was held |
| `ChapterPosition` | `string?` | 128 | Officer role, free text |
| `PtrPlaceIssued` | `string?` | 128 | From the PTR receipt |
| `PtrDateIssued` | `DateOnly?` | — | From the PTR receipt |

Migration `20260808..._AddChapterOfficerAndPtrIssuance` — four `AddColumn`s, no backfill.

**Three new Membership Type options:** `New`, `Renewal`, `Senior Citizen`, added alongside the
existing `Regular Member` in both `MemberTypes.cs` and its frontend mirror.

**Five field controls added to each of three surfaces** — the registration wizard, the member's own
profile tabs, and the admin member form — matching how every other `Member` field is handled.

## Decisions

Each of these was a genuine fork resolved by the user during planning, not a default:

- **All chapters, not NCR-only.** The request originally read "in NCR Chapter"; confirmed to mean
  every chapter. The fields render unconditionally — no chapter-gated visibility.
- **`ChapterYear` + `ChapterPosition` mean officer role and the year held** (not year-joined).
- **`Regular Member` stays** as a fourth option rather than being replaced. All 7 existing members
  carry it; keeping it means zero data migration and no record displaying a value its own edit form
  doesn't offer.
- **PTR Place/Date Issued are optional.** `PtrNumber` remains the only one of the three required to
  submit. Making them required would retroactively mark existing complete profiles as incomplete.
- **All three surfaces**, not wizard-only — anything less means data a member enters at registration
  that they can never afterwards see or correct.

## Design Notes

- **`Position` was already taken.** `Member.Position` is the member's *employment* position on the
  Professional Information tab. The new field is `ChapterPosition`; the pairing is `Chapter*` so the
  distinction is visible at every call site.
- **No server-side whitelist existed.** `MemberTypes.All` and `Chapters.All` were declared but
  referenced nowhere outside the seeder — both columns are free text capped at 64 chars. Adding the
  three options therefore required no validation or schema work, only the constants and dropdowns.
  This is pre-existing behaviour, left as-is rather than tightened as a drive-by.
- **The officer pair is not locked post-submission.** `MemberService`'s `!isDraft` guard covers only
  `MemberType`/`Chapter`. An officer post describes a role the member holds, not their eligibility,
  so it stays self-service editable — a member elected mid-term records it without an admin
  round-trip. A test pins this distinction.
- **`ChapterYear` is range-checked (1900–2200) on the self-service path only**, matching the
  existing `YearsOfPractice` precedent. Unlike the length checks this isn't guarding against a
  `DbUpdateException` — an int column holds anything — so it's a sanity guard, and the admin paths
  trust the admin exactly as they already do for `YearsOfPractice`.
- **Not folded into `hasProfessionalInfo`.** These aren't professional-info fields, and adding them
  to that predicate would silently shift every existing member's profile-completeness percentage.
- **`isValidChapterYear` lives in a new shared `core/utils/memberFields.ts`** rather than being
  triplicated across the three forms, following the `passwordPolicy.ts` precedent. (The existing
  phone validators *are* duplicated across two files; deliberately left alone as out of scope.)
- **The profile's Membership group gains its first editable fields.** It was entirely read-only
  (Membership Type / Chapter / Date Joined, all locked post-submission). Splitting the officer pair
  away from the Chapter it describes would have been worse, so they sit there under a distinct
  "Chapter Officer" sub-label.
- **The wizard's Step 3 review summary is unchanged** — it's a curated highlights list, not a full
  field dump, and all four new fields are optional.

## Known Limitation

New and Renewal describe the *application*; Senior Citizen describes the *person*. A senior
renewing their membership can only express one of the two in a single dropdown. The user was shown
a two-field alternative (New/Renewal select + Senior Citizen checkbox) and chose the single
dropdown to match the paper form. Recorded here rather than solved; revisit if it causes trouble in
practice.

## Out of Scope

Fee changes. Senior Citizen status normally implies a discount, but `ReceiptGenerator`'s
₱1,500 membership + ₱200 shipping is a flat constant and no discount was requested. Flagged as a
likely follow-up.
