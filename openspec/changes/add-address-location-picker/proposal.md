# Change: Add Philippine Address Cascading Dropdowns + Country

## Status

**Implemented.** Design approved 2026-08-08 through collaborative brainstorming; codebase
researched via a dedicated Explore pass, source data (`ph_locations_flat.json`) inspected directly
rather than assumed. Built and verified the same day — backend build clean, 294 tests passing,
frontend typecheck/lint/build clean, and the bundled data confirmed to code-split into its own
chunk rather than joining the main bundle. See `tasks.md` for what remains unverified (the live
browser pass).

## Why

`Member`'s residence and mailing addresses (`Street`/`Barangay`/`CityMunicipality`/`Province`/
`ZipCode`, ×2) are free-text `<input>` fields in three separate, hand-duplicated form surfaces —
the registration wizard (`MembershipApplicationWizardCard.tsx:689-781`), the post-submission
profile tab (`ContactInformationSection.tsx:323-475`), and the admin edit form
(`MemberFormCard.tsx:426-485`, which has no `required` and no client validation at all). None of
the three validate City/Province against anything real; a member can type any string into either
field, including a City that doesn't belong to the Province they picked, or no relationship between
the two at all. There is no format check on ZIP anywhere, and no Country field exists at all —
every member is implicitly assumed Philippines-based with no way to record otherwise.

This is also a documented, pre-existing deferral: `openspecs/members.md` already notes *"Map-based
address picker shown in the mockup is not collected... residence/mailing address is instead
collected as plain structured text fields"* — this change is the direct follow-up to that note,
using PSGC-derived reference data instead of a Maps API.

## What Changes

### 1. Bundled Philippine location data, not a lookup API

`apps/web/src/data/ph-locations.json` — the user-supplied flat dataset (1,647 cities/
municipalities, 87 provinces, 17 regions, ZIP codes for 1,571/1,647 = 95.4%), dynamic-`import()`ed
only when an address field actually renders, not part of the main bundle. A small utility module
derives `regions()` / `provincesFor(region)` / `citiesFor(region, province)` /
`zipFor(region, province, city)` from in-memory maps built once on load.

**No backend API.** This project's only existing reference-data precedent (`Chapter`/`MemberType`)
is hardcoded-in-app, not fetched — there is no `GET /api/lookup*`-shaped endpoint anywhere in this
app's 7 controllers to be consistent with. Building one here would be new architecture for
completely static, admin-unmanaged, PSGC-derived data that the frontend needs to hold in full to
cascade instantly regardless. See "Rejected Alternatives" for the fuller comparison.

**No schema change for Region/Province/City.** `Member.Province`/`CityMunicipality` are already
free-text `string?` and stay that way — the cascading selects constrain *what gets typed*, not
*where it's stored*. Existing member rows need no backfill or migration for these two columns.

### 2. New Country / MailingCountry columns

The one piece of this change that *is* a schema change: `Member.Country` and
`Member.MailingCountry` (`string?`, `HasMaxLength(64)`, matching `Province`'s own length), sourced
from a small embedded `apps/web/src/data/countries.json` (ISO-3166 short names, ~195 entries), not
the PSGC dataset — countries are outside what the user's Philippine location file covers.

- Rendered as a native `<select>`, same choice as City (see UX section below), defaulted to
  "Philippines" pre-selected — not locked, since a professional society can plausibly have an
  overseas-resident member.
- Migration **backfills `Country = 'Philippines'`** for every existing member row that already has
  a non-null `Province` or `CityMunicipality` (i.e., anyone with real address data on file) —
  leaving those blank would read as an unanswered field on every profile that predates this change,
  for data that's true for the overwhelming majority. Rows with no address data yet (blank drafts)
  stay `null`, same as every other unset address field, until the frontend's default fills them in
  through the normal save path.
- Added to `MemberDto`, `CreateMemberRequest`, `UpdateMemberRequest`, `UpdateMyProfileRequest` —
  the same 4-DTO pattern every other address field already follows — and to
  `ValidateMemberFieldLengths` and the `SubmitMyProfileAsync` required-field list, alongside the
  other core address fields it now sits beside.

### 3. UX: native `<select>`, no combobox

No searchable-select/combobox library exists anywhere in this codebase or its dependencies — every
dropdown today is a plain native `<select>`. Building or adding one is real scope beyond wiring up
cascading fields. The largest province (Cebu, 53 cities) and the country list (~195 entries) are
both cascaded/pre-filtered before the user reaches them, and native `<select>` already supports
type-ahead-jump in every browser. Ships as native `<select>` for both City and Country; a
searchable combobox is a deferred fast-follow if real usage shows it's needed (see Open Questions).

### 4. Shared `PhilippineAddressFields` component

The 12 address fields (6 residence + 6 mailing) are currently hand-duplicated verbatim across all
three surfaces with no shared component — meaning the cascade would otherwise need to be built
three separate times. Extracting `PhilippineAddressFields` (parameterized by field-prefix, so the
same component serves both residence and mailing) fixes this pre-existing duplication as a natural
part of doing the cascade properly, rather than as unrelated scope creep — each of the three form
surfaces goes from ~150 lines of hand-written address JSX to one shared component call.

### 5. ZIP behavior unchanged in spirit, sourced differently

Auto-fills from `zipFor(...)` when City is selected; stays a normal editable `<input>`, never
disabled — correctable for the 76 cities with no mapped ZIP and for genuinely multi-ZIP cities. No
new format validation is added; free text stays free text, exactly as today.

## Impact

- **Affected specs**: `openspecs/members.md`'s "Membership Application Form fields" and
  "Registration flow" sections need updating once this ships — the address fields stop being
  unconstrained free text, and the standing "map-based picker... not collected" deferral note is
  resolved by this change, not merely narrowed.
- **Affected code**: `Member.cs`, `MemberConfiguration.cs`, one new migration, `MemberDto.cs` (4
  records), `MemberService.cs` (length validation + submit-required list),
  `MembershipApplicationWizardCard.tsx`, `ContactInformationSection.tsx`, `MemberFormCard.tsx`, two
  new bundled data files, one new shared component, one new lookup-utility module.
- **No API surface change** — no new controller, no new route.
- **Existing member data**: `Province`/`CityMunicipality`/`ZipCode` values already on file are
  untouched (still valid free text, now just re-editable through a constrained UI going forward).
  `Country`/`MailingCountry` are backfilled per the rule in section 2.

## Rejected Alternatives

- **DB-seeded `Region`/`Province`/`City` tables + a `GET /api/locations/*` lookup API.** Heavier to
  build (new entities, `IEntityTypeConfiguration`s, `DbSet` registrations, a seeder reading from an
  external file — no existing seeder does that, both current seeders use literal in-code arrays,
  and a new controller), for data that's completely static and that the frontend needs to hold in
  full anyway to cascade without a network round-trip per selection. Would also be the first
  reference-data lookup endpoint in an app whose only existing precedent (Chapter/MemberType) is
  the opposite pattern — hardcoded, not fetched.
- **Storing PSGC codes alongside names.** The supplied flat dataset has no codes (by deliberate
  choice — the coded files `ph_regions.json`/`ph_provinces.json`/`ph_cities.json` were available
  and not used). Storing codes would mean either switching datasets or inventing synthetic ones
  with no external meaning, for no benefit this change needs. Revisit only if a future integration
  genuinely requires PSGC codes.
- **Searchable combobox for City/Country.** No such component exists in this codebase; building one
  is disproportionate scope for lists that are already pre-filtered to a browsable size by the
  preceding cascade level.

## Open Questions

- If real-world usage shows Cebu's 53-city (or the 195-country) native `<select>` is genuinely
  awkward, a combobox becomes its own follow-up change — not blocking this one.
- Whether `openspecs/members.md`'s existing "no Maps API integration" note should be rewritten or
  simply marked resolved — a wording call for whoever updates that doc, not a design decision.
