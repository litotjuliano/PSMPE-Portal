# Chapters

## Purpose
The PSMPE chapters (NCR, Cebu, Davao, ...) offered on member and event forms, filters and the
dashboard chart. A Super Admin maintains them in the portal, so a new or renamed chapter needs no
deploy. Members and events point at a chapter by id (`ChapterId`), so a rename shows up everywhere
at once. A chapter that is still in use cannot be deleted - it is deactivated instead.

## Endpoints
- `GET /api/chapters` — every chapter, active and inactive, ordered by `SortOrder` then name
  - Auth: authenticated; also readable by members who haven't been approved yet (`[AllowExpiredMember]`),
    because applicants pick a chapter on the membership application
  - Response: `[{ id, name, sortOrder, isActive, memberCount, eventCount }]`. Inactive ones are included
    (flagged) so a screen showing a member's saved chapter can still name it; dropdowns filter to
    `isActive` themselves, keeping a member's/event's current chapter selectable.

All endpoints below are Auth: policy `RequireSuperAdmin`, under `/api/admin/chapters`.

- `GET /?search=&isActive=` — same shape; `search` is a case-insensitive name match, `isActive` true/false/omitted
- `POST /` `{ name, sortOrder?, isActive? }` — blank `sortOrder` puts it last; `isActive` defaults to true
- `PUT /{id}` `{ name, sortOrder?, isActive? }` — omitted `sortOrder`/`isActive` are left unchanged
- `DELETE /{id}` — 204; 409 while any member or event references it, with a message suggesting Deactivate

Errors: 400 blank name or over 64 characters, 404 unknown id, 409 duplicate name (case-insensitive) or
deleting a chapter in use. Body is `{ message }`.

## Authorization rules
- Reading is for any signed-in user; every write is Super Admin only (no separate permission), same as Locations.
- Page: `/admin/chapters` (sidebar "Chapters"), Super Admin only in both the router and the menu.
- No chapter-level access scoping: chapter officers get no extra or restricted access (still out of scope).

## Data
- Table `Chapters` (`Name` unique, max 64; `SortOrder`; `IsActive`). `Members.ChapterId` (nullable - a
  draft application with no chapter yet) and `Events.ChapterId` (nullable = national / all chapters),
  both foreign keys with `Restrict`.
- Migration `AddChaptersAndOptionalEventDates` seeds the original six (NCR, Cebu, Davao, Baguio, Cavite,
  Quezon City Chapter, in that order), backfills `ChapterId` from the old text columns
  (case/whitespace-insensitive), then drops those columns. Members had no server-side whitelist, so any
  other distinct value found becomes an **inactive** chapter (sort order 101+) rather than orphaning
  the people on it; admins can rename or merge by editing those members.
- Members/events API payloads send `chapterId`; read DTOs return both `chapterId` and `chapter` (the name).
- **Validation:** a chapter id must exist. An inactive chapter can't be newly chosen, but a member/event
  already on it can be re-saved. After a member's application is submitted the chapter is locked
  (compares ids) - only an admin can change it, as before.
- **Dashboard** `ByChapter` lists every active chapter (zero-filled, admin-set order) plus any inactive
  chapter that still has members.

## Open questions / TODO
- Edits are not written to the audit log.
- Merging two chapters (moving everyone from one to another) is not built; edit the members, then delete.
- No chapter filter on the Members list (still deferred, see `members.md`).
