# Change: Super Admin CRUD for chapters, and event form fixes

## Status

**Implemented** 2026-10-21 (backend, admin page, forms switched to the API, tests green). Verified
against the real local database (migration applied, dashboard counts unchanged, event rules exercised
through the live API). Not yet verified in a live browser or on staging/production.

## Why

1. Chapters were a hardcoded list of six names (`Chapters.cs`, copied by hand into `member.ts`), stored
   as free text on every member and event. Adding or renaming one needed a deploy.
   `openspecs/members.md` had already flagged this: "revisit as a real entity+table if admins ever need
   to add/rename chapters without a deploy".
2. The Event form froze on "Saving…" when Starts At / Ends At were blank (`new Date('').toISOString()`
   threw after the form marked itself as saving, with nothing sent). Production hit the same bug.
3. Requested for the Event form: only Title and Description required for now; Venue as a textarea.

## What changes

- New `Chapters` table (`Name`, `SortOrder`, `IsActive`). `Member.Chapter`/`Event.Chapter` text columns
  become `ChapterId` foreign keys. One data-preserving migration seeds the six, backfills, keeps stray
  values as inactive chapters, then drops the old columns.
- Super Admin CRUD under `/api/admin/chapters` and a `/admin/chapters` page (search + active/inactive
  filter); `GET /api/chapters` feeds every chapter dropdown (member forms, wizard, events form and filter).
- Event form: Title and Description required (Save Draft and Publish); Starts At / Ends At required only
  to Publish; Venue is a textarea. `Event.StartsAt/EndsAt` are nullable for Drafts. Scheduling a Draft
  creates its default session.
- Decisions: real foreign key (rename updates everywhere); delete blocked while in use with a Deactivate
  option; CRUD only - no chapter-officer access scoping; dates optional only for Drafts (a published
  event always has dates, so registration, attendance, CPD and certificates are untouched).
- Behavior to know: existing live events with no description need one the next time they are edited.
  Dates can't be cleared on an event that already has registrations.

See `openspecs/chapters.md` for the endpoint contract.
