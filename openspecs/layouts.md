# Layouts

## Purpose

Reusable page layouts (`PSMPE.Portal.Domain.Entities.Layout`) that `ContentItem`s can
reference. Some layouts ship with the platform ("system layouts") and must not be
deletable by regular users or even Admins — only a Super Admin can remove them.

## Endpoints

- `GET /api/layouts` — list all layouts
  - Auth: authenticated
- `POST /api/layouts` — create a layout
  - Auth: authenticated
  - Request: `{ name, definition }`
  - Always created as a non-system layout owned by the caller.
- `DELETE /api/layouts/{id}` — delete a layout
  - Auth: authenticated (ownership/system-layout checks happen inside `LayoutService`)

## Authorization rules

- **System layouts** (`IsSystemLayout == true`, seeded on first run, `OwnerId == null`):
  only a `Super Admin` may delete them. Not even `Admin` can.
- **Non-system layouts**: the owner, or an `Admin`/`Super Admin`, may delete them.

This is the concrete example of the platform-wide rule: *"users can manage their own
content, but cannot delete system layouts or perform system-wide administrative
actions."* See `PSMPE.Portal.Application.Layouts.LayoutService.DeleteAsync` and
`PSMPE.Portal.Infrastructure.Authorization.SystemAdminAuthorizationHandler`.

## Open questions / TODO

- No update endpoint yet (only create/list/delete) — add one following the same
  ownership rules as `DeleteAsync` if layout editing is needed.
