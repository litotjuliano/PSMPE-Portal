# Tasks: add-address-location-picker

**Goal:** Replace free-text City/Province address fields with cascading Region → Province → City
dropdowns (ZIP auto-filled, still editable), and add a Country field (dropdown, defaulted to
Philippines) — across the registration wizard, the profile edit tab, and the admin edit form.

**Architecture:** No backend lookup API. Philippine location data ships as a bundled, dynamically-
imported JSON asset (`apps/web/src/data/ph-locations.json`, the user-supplied flat dataset) with a
small in-memory-map utility deriving the cascade. Country is the one genuine schema addition —
two new nullable columns on `Member`, sourced from a separate small embedded ISO-3166 dataset. A
new shared `PhilippineAddressFields` component replaces the currently-triplicated address JSX in
all three form surfaces.

**Tech Stack:** React 19 + Vite + TypeScript (frontend), .NET 8 + EF Core + Postgres (backend, for
the Country columns only). No test runner exists in `apps/web`; backend has xUnit integration/unit
test projects.

**Before starting:** read `proposal.md` in this folder.

---

## 1. Country/MailingCountry schema

**Files:**
- Modify: `src/PSMPE.Portal.Domain/Entities/Member.cs`
- Modify: `src/PSMPE.Portal.Infrastructure/Persistence/Configurations/MemberConfiguration.cs`
- Create: one new EF migration

- [x] **1.1 Add the two columns to the entity**

In `Member.cs`, alongside the existing address block (`:26-43`), add:

```csharp
public string? Country { get; set; }
public string? MailingCountry { get; set; }
```

- [x] **1.2 Configure max length**

In `MemberConfiguration.cs`, alongside the existing address `HasMaxLength` calls (`:33-46`):

```csharp
builder.Property(m => m.Country).HasMaxLength(64);
builder.Property(m => m.MailingCountry).HasMaxLength(64);
```

64, matching `Province`'s own length — comfortably fits any standard country name.

- [x] **1.3 Generate the migration**

```bash
dotnet ef migrations add AddMemberCountry --project src/PSMPE.Portal.Infrastructure --startup-project src/PSMPE.Portal.WebAPI
```

- [x] **1.4 Add the backfill to the migration's `Up()`**

After the generated `AddColumn` calls, backfill existing members who already have address data —
leaving those blank would read as an unanswered field on every profile that predates this change:

Match this project's own raw-SQL migration style (triple-quoted C# raw string, not `@"..."` — see
`20260727104014_ConvertMemberUploadKindToString.cs` for the precedent):

```csharp
migrationBuilder.Sql(
    """
    UPDATE "Members"
    SET "Country" = 'Philippines'
    WHERE "Country" IS NULL AND ("Province" IS NOT NULL OR "CityMunicipality" IS NOT NULL);
    UPDATE "Members"
    SET "MailingCountry" = 'Philippines'
    WHERE "MailingCountry" IS NULL AND ("MailingProvince" IS NOT NULL OR "MailingCityMunicipality" IS NOT NULL);
    """);
```

Rows with no address data yet (blank drafts) stay `null`, same as every other unset address field.

- [ ] **1.5 Commit**

```bash
git add src/PSMPE.Portal.Domain/Entities/Member.cs src/PSMPE.Portal.Infrastructure/Persistence/Configurations/MemberConfiguration.cs src/PSMPE.Portal.Infrastructure/Persistence/Migrations/
git commit -m "Add Country and MailingCountry columns to Member"
```

---

## 2. DTOs and service validation

**Files:**
- Modify: `src/PSMPE.Portal.Application/Members/Dtos/MemberDto.cs`
- Modify: `src/PSMPE.Portal.Application/Members/MemberService.cs`

- [x] **2.1 Add `Country`/`MailingCountry` to all four DTOs**

Same 4-record pattern every other address field already follows (`MemberDto` `:21-32`,
`CreateMemberRequest` `:88-99`, `UpdateMemberRequest` `:142-153`, `UpdateMyProfileRequest`
`:203-214`) — add `string? Country` / `string? MailingCountry` alongside each existing
`Province`/`MailingProvince` line.

- [x] **2.2 Map them in every read/write path**

`ToDto`, `CreateAsync`, `UpdateAsync`, `UpsertMyProfileAsync` in `MemberService.cs` each already
assign the other 12 address fields one by one — add `Country`/`MailingCountry` alongside
`Province`/`MailingProvince` in each.

- [x] **2.3 Add length validation**

In `ValidateMemberFieldLengths` (`:593-647`), add `Country`/`MailingCountry` to the parameter list
and the length-check calls, alongside the existing `Province`/`MailingProvince` checks (max 64).

- [x] **2.4 Add to the submit-required list**

In `SubmitMyProfileAsync` (`:488-565`), add `Country` to the required-field check alongside
`Street`/`Barangay`/`CityMunicipality`/`Province`/`ZipCode`. In normal use this never actually
blocks a submit, since the frontend pre-selects "Philippines" by default — this is a safety net for
a caller that skipped the field entirely (e.g. a future non-wizard integration), not a real gate
for the wizard's own users. `MailingCountry` is **not** required, matching how mailing address
fields already aren't required when `mailingSameAsResidence` covers them.

- [ ] **2.5 Commit**

```bash
git add src/PSMPE.Portal.Application/Members/Dtos/MemberDto.cs src/PSMPE.Portal.Application/Members/MemberService.cs
git commit -m "Thread Country/MailingCountry through the member DTOs and validation"
```

---

## 3. Bundled data + lookup utility

**Files:**
- Create: `apps/web/src/data/ph-locations.json`
- Create: `apps/web/src/data/countries.json`
- Create: `apps/web/src/core/utils/phLocations.ts`

- [x] **3.1 Add the data files**

Copy the user-supplied `ph_locations_flat.json` to `apps/web/src/data/ph-locations.json` verbatim
(1,647 entries, `{ region, province, city, zip_code }`, `zip_code` null for 76 of them — this is
expected, not a bug, see `proposal.md`).

Create `apps/web/src/data/countries.json` — a standard ISO-3166 short-name list (~195 entries,
plain `string[]`, alphabetically sorted), with `"Philippines"` present as a normal entry (the
"default" behavior lives in the component, not in the data file).

- [x] **3.2 Build the lookup utility**

Create `apps/web/src/core/utils/phLocations.ts`:

```ts
export interface PhLocationRow {
  region: string
  province: string
  city: string
  zip_code: string | null
}

let rows: PhLocationRow[] | null = null

async function load(): Promise<PhLocationRow[]> {
  if (!rows) {
    rows = (await import('../../data/ph-locations.json')).default as PhLocationRow[]
  }
  return rows
}

export async function regions(): Promise<string[]> {
  const data = await load()
  return [...new Set(data.map((r) => r.region))]
}

export async function provincesFor(region: string): Promise<string[]> {
  const data = await load()
  return [...new Set(data.filter((r) => r.region === region).map((r) => r.province))]
}

export async function citiesFor(region: string, province: string): Promise<string[]> {
  const data = await load()
  return data.filter((r) => r.region === region && r.province === province).map((r) => r.city)
}

export async function zipFor(region: string, province: string, city: string): Promise<string | null> {
  const data = await load()
  return data.find((r) => r.region === region && r.province === province && r.city === city)?.zip_code ?? null
}
```

Dynamic `import()` inside `load()`, not a static top-level import — the ~168KB (minified) dataset
only loads when an address field actually renders, not on every page load for users who never
touch this form (e.g. an admin browsing `/members`).

- [ ] **3.3 Commit**

```bash
git add apps/web/src/data/ apps/web/src/core/utils/phLocations.ts
git commit -m "Add bundled PH location data and a cascading lookup utility"
```

---

## 4. `PhilippineAddressFields` shared component

**Files:**
- Create: `apps/web/src/integrations/template/components/shared/PhilippineAddressFields.tsx`

- [x] **4.1 Build the component**

Props: `{ prefix: 'residence' | 'mailing', values: {...}, onChange, editing }` (or equivalent
shaped to whatever each of the three call sites' existing state/`onChange` pattern looks like —
match the surrounding file's convention rather than inventing a new one). Renders, in order:
House No. (`<input>`, unchanged, optional), Street (`<input>`, unchanged), Barangay (`<input>`,
unchanged), Region (`<select>`, populated via `regions()`), Province (`<select>`, populated via
`provincesFor(region)`, disabled/empty until Region is chosen), City (`<select>`, populated via
`citiesFor(region, province)`, disabled/empty until Province is chosen), ZIP Code (`<input>`,
auto-filled via `zipFor(...)` on City change but always editable), Country (`<select>`, from
`countries.json`, defaulted to `"Philippines"`).

Changing Region clears the current Province/City selections (and re-triggers the ZIP lookup to
empty); changing Province clears City. This is the one behavioral difference from today's plain
text inputs — call it out clearly in the component's own doc comment, since it's a UX change a
future maintainer needs to understand is deliberate, not a bug.

- [ ] **4.2 Commit**

```bash
git add apps/web/src/integrations/template/components/shared/PhilippineAddressFields.tsx
git commit -m "Add the shared Philippine address cascade component"
```

---

## 5. Wire into the three form surfaces

**Files:**
- Modify: `apps/web/src/integrations/template/pages/MembershipApplicationWizardCard.tsx:689-781`
- Modify: `apps/web/src/integrations/template/pages/profile-sections/ContactInformationSection.tsx:323-475`
- Modify: `apps/web/src/integrations/template/pages/MemberFormCard.tsx:252-267,426-485`

- [x] **5.1 Wizard** — replace the residence block (`:692-720`) and mailing block (`:722-781`) each
  with one `<PhilippineAddressFields prefix="residence" .../>` / `prefix="mailing"` call. Keep the
  existing `mailingSameAsResidence` checkbox and its show/hide logic exactly as-is around the
  component.

- [x] **5.2 Profile tab** — same replacement in `ContactInformationSection.tsx`, both the
  edit-mode inputs and the view-mode `<span>` display (the component should itself branch on an
  `editing` prop the same way the rest of this file's fields already do, rather than the caller
  branching between two different renders).

- [x] **5.3 Admin form** — same replacement in `MemberFormCard.tsx`, both `ViewField` read-only
  display (`:252-267`) and the edit-mode inputs (`:426-485`). This form currently has **no**
  `required` on any address field and no client validation at all — preserve that (the component's
  cascade behavior applies regardless, but don't newly add a `required` HTML attribute here that
  wasn't there before, since admin edits are already looser than the self-service wizard by
  design).

- [x] **5.4 Verify no field was dropped or duplicated**

Cross-check: every field that existed in the old free-text blocks (House No./Street/Barangay/City/
Province/Zip, ×2) plus the two new Country fields should now render in exactly one place per
surface — same kind of check done for the earlier profile-tab regroup in this project.

- [ ] **5.5 Commit**

```bash
git add apps/web/src/integrations/template/pages/MembershipApplicationWizardCard.tsx apps/web/src/integrations/template/pages/profile-sections/ContactInformationSection.tsx apps/web/src/integrations/template/pages/MemberFormCard.tsx
git commit -m "Wire the address cascade into the wizard, profile, and admin forms"
```

---

## 6. Docs

**Files:**
- Modify: `openspecs/members.md`

- [x] **6.1** Update "Membership Application Form fields" to describe the cascading Region/
  Province/City/Country selects instead of plain structured text fields, and note the ZIP
  auto-fill-but-editable behavior.
- [x] **6.2** Resolve or rewrite the "Map-based address picker... not collected" deferral note —
  this change is its direct follow-up, not a further narrowing of the same gap.
- [ ] **6.3 Commit** alongside or immediately after the code commits, matching this project's
  established pattern of keeping `openspecs/*.md` in sync with what's actually shipped.

---

## 7. Verify

No test runner exists in `apps/web`; this is lint, build, `dotnet test`, and a live browser pass.

1. `dotnet build src/PSMPE.Portal.sln` and `dotnet test src/PSMPE.Portal.sln` — 0 errors.
2. `cd apps/web && npm run lint && npm run build` — 0 errors, only the known pre-existing warnings.
3. **Migration applies cleanly** against a copy of real data — confirm existing members with
   address data get `Country = 'Philippines'` backfilled; confirm a member with no address data
   stays `null`.
4. **Cascade behavior** in all three surfaces: Region populates Province, Province populates City,
   City auto-fills ZIP (editable after), changing Region/Province clears the levels below it.
5. **The 76 no-ZIP cities** — pick one, confirm ZIP stays blank and freely typeable rather than
   erroring or blocking.
6. **NCR specifically** — City of Manila's districts (Tondo I/II, Binondo, Quiapo, etc.) render
   correctly as the "city" level; this is the source data's actual structure, not a bug to fix.
7. **Country defaults to Philippines** on a fresh wizard start, and is changeable.
8. **Existing member data is preserved** — open a member who registered before this change and
   confirm their existing Province/City/ZIP values still display correctly in the new selects
   (i.e. the free-text value they saved happens to match an option in the bundled list) or, if it
   doesn't exactly match, that this is visible/correctable rather than silently blanked.
9. **Bundle size** — confirm via browser devtools Network tab that `ph-locations.json` and
   `countries.json` only load when an address field actually renders (Contact Information step/tab
   open), not on initial app load.
10. Admin form (`MemberFormCard.tsx`) still has no `required` on address fields — confirm this
    wasn't accidentally tightened.
