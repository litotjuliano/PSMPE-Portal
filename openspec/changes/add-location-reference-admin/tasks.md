# Tasks

- [x] Entities, EF configurations, migration `AddPhLocations`
- [x] `LocationSeeder` (embedded JSON, empty-table only) wired after `MigrateAsync`
- [x] `LocationService` + `LocationsController` + `AdminLocationsController` (Super Admin)
- [x] Unit tests (`LocationServiceTests`) and integration tests (`AdminLocationsControllerTests`)
- [x] `locationsApi`, `phLocations.ts` reads the API, `invalidateLocations()`
- [x] `AdminLocationsPage`, `LocationsTable`, `LocationFormModal`, route + Super-Admin menu item
- [x] `openspecs/locations.md`, `openspecs/members.md` updated
- [ ] Live browser pass as Super Admin / Admin / Member against a real database
- [ ] Confirm the seed ran on staging before promoting to production
- [ ] Optional later: audit-log entries for edits, CSV bulk import
