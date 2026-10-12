# Locations

## Purpose
The Philippine regions, provinces and cities/municipalities (with zip codes) offered by the address
picker. A Super Admin maintains them in the portal, so a new or renamed place needs no deploy. Member
addresses store plain text, not references to these rows, so editing or deleting a place never changes
an address a member already saved.

## Endpoints
- `GET /api/locations` — every city as a flat row, ordered by region order, province, city
  - Auth: authenticated; also readable by members who haven't been approved yet (`[AllowExpiredMember]`),
    because they fill in the membership application
  - Response: `[{ region, province, city, zipCode | null }]`; cached server-side (`Cache:LocationDurationSeconds`,
    default 300s) and evicted after every admin write

All endpoints below are Auth: policy `RequireSuperAdmin`, under `/api/admin/locations`.

- `GET /regions?search=` — all regions (unpaged), `[{ id, name, sortOrder, provinceCount }]`
- `POST /regions` `{ name, sortOrder? }` — blank `sortOrder` puts the region last
- `PUT /regions/{id}` `{ name, sortOrder? }`
- `DELETE /regions/{id}` — 409 while it still has provinces
- `GET /provinces?page&pageSize&search&regionId` — paged, `[{ id, name, regionId, regionName, cityCount }]`
- `POST /provinces` `{ name, regionId }`, `PUT /provinces/{id}`, `DELETE /provinces/{id}` — 409 while it still has cities
- `GET /cities?page&pageSize&search&regionId&provinceId` — paged; `search` matches name or zip;
  `[{ id, name, zipCode, provinceId, provinceName, regionId, regionName }]`
- `POST /cities` `{ name, zipCode?, provinceId }`, `PUT /cities/{id}`, `DELETE /cities/{id}`

Errors: 400 blank name or unknown parent, 404 unknown id, 409 duplicate name under the same parent
(case-insensitive) or deleting a parent that still has children. Body is `{ message }`.

## Authorization rules
- Reading is for any signed-in user; every write is Super Admin only (no separate permission).
- Page: `/admin/locations` (sidebar "Locations"), Super Admin only in both the router and the menu.

## Data
- Tables `PhRegions` (`SortOrder` keeps "Region I … NCR" order), `PhProvinces`, `PhCities`; unique
  name per parent; foreign keys are `Restrict`.
- `LocationSeeder` loads the embedded `ph-locations.json` (1,646 cities) once, only into an empty
  table, right after migrations in the `Seed:Enabled` startup block. Edits are never overwritten.
- Zamboanga Del Sur seeds as 27 places (Pagadian City + 26 municipalities); Zamboanga City is not
  seeded, a Super Admin can add it.

## Open questions / TODO
- Edits are not written to the audit log.
- Countries are still a bundled file, not managed here.
- No bulk import (CSV) for adding many places at once.
