# Changelog

All notable, user-facing changes to the PSMPE Portal are recorded here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow
[Semantic Versioning](https://semver.org/). See
`openspec/changes/add-release-versioning/proposal.md` for how versions are cut and where the
running app's own footer/"What's New" card get their copy from (`apps/web/src/core/data/releaseNotes.ts`).

## [Unreleased]

### Added

- Admins can now resend a verification email to an unverified member from the Users page (a mail
  icon next to each unverified account), instead of the member having to request it themselves.

## [1.0.2] - 2026-10-07

### Fixed

- "Forgot password" and the admin "Send password reset" action now report a temporary email
  failure clearly instead of a generic error, and approving a member no longer shows an error
  when only the approval email fails — the approval itself had already succeeded.

## [1.0.1] - 2026-10-06

### Fixed

- If the verification email can't be sent when someone registers (e.g. the email provider is
  down), they now see a clear "your account was created, but we couldn't send the email — try
  again shortly" message instead of a generic error, and "Resend verification email" reports a
  temporary failure instead of silently doing nothing.

## [1.0.0] - 2026-08-31

First tagged release — the app was already live in production before this; this establishes
traceability going forward rather than marking a new milestone.

### Added

- Members can add the Portal Access add-on mid-cycle, without waiting for their next renewal —
  a standalone "Add Portal Access" card on the Profile page for anyone current on dues but
  missing the add-on.

### Fixed

- A newly registered account with no membership application yet no longer gets unrestricted
  portal access (including event registration) — it's now correctly restricted the same way an
  Expired or portal-access-less account is.
- The sidebar no longer collapses to "My Profile" only for a restricted member — every
  role-appropriate nav item stays visible, matching how the app behaves for anyone else.
- A restricted member is no longer redirected away from every page but Profile — pages stay
  reachable; the restriction now shows up as a disabled action (e.g. the Events page's Register
  button) with a message explaining why, instead of bouncing the whole page away.
- The Super Admin role's permissions are reconciled on every startup, so a newly added
  permission (e.g. Events) is never silently missing for that role.
