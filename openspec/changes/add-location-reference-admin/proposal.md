# Change: Super Admin CRUD for Philippine locations

## Status

**Implemented** 2026-10-12 (backend, admin page, picker switched to the API, tests green).
Not yet verified in a live browser against a real database.

## Why

The address picker read a bundled `ph-locations.json`, so any new, renamed or removed city needed a
code change and a deploy (Pagadian / Zamboanga Del Sur had to be fixed by hand in v1.2.1). The Super
Admin should be able to maintain the list.

## What changes

- New tables `PhRegions`, `PhProvinces`, `PhCities` (migration `AddPhLocations`), seeded once from the
  old JSON (now an embedded resource) into an empty table only.
- `GET /api/locations` (flat rows, cached) feeds the picker; `core/utils/phLocations.ts` indexes it and
  is invalidated after admin edits. `countries.json` stays bundled.
- Super Admin CRUD under `/api/admin/locations/{regions,provinces,cities}` and a `/admin/locations`
  page with search and region/province filters.
- Decisions: manage all three levels; member addresses stay plain text, so edits and deletes never
  touch saved addresses; deleting a parent with children is refused (409).

See `openspecs/locations.md` for the endpoint contract.
