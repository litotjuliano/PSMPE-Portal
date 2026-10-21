# Tasks: Chapters admin and event form fixes

## Backend
- [x] `Chapter` entity, `ChapterConfiguration`, `DbSet` on the context and `IApplicationDbContext`
- [x] `Member.ChapterId` and `Event.ChapterId` foreign keys (Restrict); `Event.StartsAt/EndsAt` nullable
- [x] Migration `AddChaptersAndOptionalEventDates` hand-ordered: create, seed six, keep stray values as inactive, backfill, drop old columns; reviewed `Down`
- [x] Migration tested on a copy of the local database with stray values planted
- [x] `ChapterService` (search, active filter, usage counts, duplicate and in-use conflicts)
- [x] `AdminChaptersController` (Super Admin) and `ChaptersController` (read, `AllowExpiredMember`)
- [x] `MemberService`: validate `ChapterId` (create, update, draft save), lock compares ids, stats by chapter table
- [x] `EventService`: Description required, dates required only to publish, default session on scheduling, chapter resolve, filter by `chapterId`, block clearing dates with registrations
- [x] `MemberSeeder` looks chapters up by name; legacy `Chapters` const class removed

## Frontend
- [x] `chaptersApi`, `core/utils/chapters.ts` (cached loader + `useChapters`)
- [x] `AdminChaptersPage`, `ChaptersTable` (search + status filter), `ChapterFormModal`; route and sidebar item
- [x] Member form, application wizard, events form and events filter use the API list; inactive chapter stays visible for its current members/events
- [x] Event form: required Title/Description, dates required to publish, Venue textarea, no more stuck "Saving…"
- [x] Undated Draft displays "Date to be set"; sessions need dates first

## Tests
- [x] `ChapterServiceTests`, `AdminChaptersControllerTests`
- [x] Stats (inactive chapter hidden/kept, admin sort order), member chapter validation, event rules (description, draft dates, publish needs dates, default session, chapter validity)
- [x] Existing member/event tests moved to `ChapterId` via `TestChapters`

## Release
- [x] `openspecs/chapters.md`, `members.md`, `events.md`, README index; CHANGELOG and in-app release notes (v1.4.0)
- [ ] Verify in a real browser (admin page, event form, member wizard, dashboard chart)
- [ ] Staging `v1.4.0-rc.1`; **dump the production database before merging to main** (the migration drops the old text columns)
- [ ] Production `v1.4.0`
