# Content

## Purpose

CRUD for CMS content items (`PSMPE.Portal.Domain.Entities.ContentItem`), enforcing that
regular users can only manage content they own.

## Endpoints

- `GET /api/content` — list all content items
  - Auth: authenticated
- `GET /api/content/{id}` — get one content item
  - Auth: authenticated
- `POST /api/content` — create a content item
  - Auth: authenticated
  - Request: `{ title, body, layoutId }`
  - `ownerId` is always set to the calling user; it cannot be supplied by the client.
- `PUT /api/content/{id}` — update a content item
  - Auth: authenticated + `ContentOwnerOrAdmin` policy (resource-based)
  - Request: `{ title, body, status, layoutId }`
- `DELETE /api/content/{id}` — delete a content item
  - Auth: authenticated + `ContentOwnerOrAdmin` policy (resource-based)

## Authorization rules

A user may update/delete a content item if **either**:
- they own it (`ContentItem.OwnerId == currentUserId`), or
- they hold the `Admin` or `Super Admin` role.

Enforced twice, deliberately: once at the controller via
`IAuthorizationService.AuthorizeAsync(User, item, "ContentOwnerOrAdmin")`
(`PSMPE.Portal.Infrastructure.Authorization.OwnershipAuthorizationHandler`), and again
inside `ContentService` itself, so the rule holds even if a future caller bypasses the
controller (e.g. a background job).

## Open questions / TODO

- Listing currently returns all content regardless of status/ownership — TODO: filter to
  "published + own" for non-admins once a visibility requirement is defined.
- No pagination yet; fine for a starter dataset, revisit before production content volume.
