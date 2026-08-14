# dashboard-previews Specification (Delta)

## ADDED Requirements

### Requirement: Event and News Preview Widgets Are Static Mock Content

The Dashboard SHALL display two preview widgets, one for Event Management and one for News
Management, each showing hardcoded mock content (3-4 plausible items with dates) with no backend
endpoint, API call, or data fetch of any kind. Neither Event Management nor News Management SHALL
be implemented as a real module by this change — the widgets exist solely to signal a planned
feature.

#### Scenario: The Events preview widget makes no network request

- **WHEN** the Dashboard renders the Events preview widget
- **THEN** no HTTP request is made on its behalf — its content is entirely local, static data

#### Scenario: The News preview widget makes no network request

- **WHEN** the Dashboard renders the News preview widget
- **THEN** no HTTP request is made on its behalf — its content is entirely local, static data

### Requirement: Preview Widgets Are Unambiguously Marked as Non-Real

Each preview widget SHALL display a visible "Preview · Coming Soon" (or equivalent explicit)
badge, AND at least one additional visual treatment distinguishing it from real dashboard content
(e.g. a dashed border and/or muted background tint) — the "not real data" signal SHALL NOT rely
on the badge text alone.

#### Scenario: A preview widget is visually distinct from real widgets

- **WHEN** a user views the Dashboard
- **THEN** each preview widget shows both the "Preview · Coming Soon" badge and a second, distinct
  visual treatment (not shared with any real/live widget on the page)

### Requirement: Preview Widgets Are Visible to Every Role

Unlike the Statistics section (Admin/staff only), the Event and News preview widgets SHALL render
for every authenticated user regardless of role, including users holding the Member role — they
are a recruitment/engagement signal aimed at members and prospective members, not an internal
admin tool.

#### Scenario: A Member sees both preview widgets

- **WHEN** a user with the Member role views the Dashboard
- **THEN** both the Events preview widget and the News preview widget render

#### Scenario: An Admin sees both preview widgets

- **WHEN** a user with an Admin/staff role views the Dashboard
- **THEN** both the Events preview widget and the News preview widget render, alongside the
  Statistics section
