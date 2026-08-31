# Change: Release Versioning for Staging/Production Merges

## Status

**Designed and implemented in one pass, 2026-08-31.** Raised by the user directly after a day of
several `develop` → `staging` → `main` merges with no way to trace which commit was actually
running where, or to identify a labeled point to revert to. Brainstormed end-to-end (versioning
scheme, staging/production relationship, footer display, changelog, in-app "What's New" surface,
merge commit naming), approved, then implemented immediately as the last thing shipped that day so
the scheme is proven working rather than sitting unused. See `tasks.md` in this folder.

## Why

Every merge to `staging`/`main` was untraceable after the fact: no git tags, no version anywhere in
the running app, generic auto-generated merge commit messages
(`Merge remote-tracking branch 'origin/develop' into staging-merge-temp5`), and nothing recording
what shipped when. If something needed reverting, there was no labeled point to revert *to* — only
raw commit SHAs, found by scrolling `git log`.

## Decisions

- **Semantic versioning, bumped by change type** — not calendar-based, not a fully-automatic
  git-describe scheme. From the last released `vX.Y.Z` tag: any `feat:` commit in the merge → minor
  bump, only `fix:`/`refactor:`/`docs:`/`chore:` → patch bump, an explicit breaking change → major
  bump. Chosen over calendar/build-number and pure git-describe schemes for being meaningful at a
  glance, at the cost of requiring a judgment call at each merge (already made by whoever's doing
  the merge, same as choosing the merge commit's summary).
- **Staging carries an `-rc.N` suffix of the version being promoted**, not an independent counter —
  `v1.4.0-rc.1` on staging becomes exactly `v1.4.0` on production once promoted, so the numbers
  themselves show which staging build became which release. `N` increments if more commits land on
  staging before promotion.
- **Tags are the only source of truth — no committed `VERSION` file.** `git describe --tags --always`
  at deploy time (run on the droplet, in the same SSH script that already does
  `git reset --hard origin/<branch>`) resolves the version with nothing to keep in sync or
  merge-conflict over.
- **Version reaches the frontend exactly like `VITE_API_BASE_URL` already does** — a Docker build
  `ARG`/`ENV` pair, set from the deploy script's `git describe` output, inlined into the Vite bundle
  at build time. No backend endpoint, no runtime lookup.
- **Merge commit messages carry the version** — `Merge develop into staging: vX.Y.Z-rc.N — <summary>`
  and `Merge staging into main: vX.Y.Z — <summary>`, replacing the generic `--no-edit` message, so
  `git log --oneline` alone reads as a release history.
- **A CHANGELOG.md at the repo root** (Keep a Changelog format) records every release, user-facing
  changes only (no `docs:`/internal-`refactor:`-only entries). An `[Unreleased]` section accumulates
  entries as work lands on staging; promoting to production retitles it to a dated version heading.
- **A matching in-app "What's New" card**, not just a repo file — `apps/web/src/core/constants/releaseNotes.ts`
  is a hand-authored structured mirror of the same CHANGELOG.md content (written in the same commit,
  not generated from it, to avoid a parser/build step), rendered by `WhatsNewWidget.tsx` on the
  Dashboard for any authenticated user. Shows only the current version's notes — no history
  browsing, no dismiss/seen-tracking, kept deliberately minimal for a first pass.
- **No backend version endpoint, no assembly version bump, no admin-facing version-management UI**
  were requested or built — scope stayed to what the user asked for (footer + traceability +
  changelog + naming), not speculative extensions.

## What Changes

- `docker-compose.yml`: `frontend.build.args` gains `VITE_APP_VERSION: ${VITE_APP_VERSION:-dev}`.
- `apps/web/Dockerfile`: matching `ARG VITE_APP_VERSION=dev` / `ENV VITE_APP_VERSION=$VITE_APP_VERSION`.
- `apps/web/src/vite-env.d.ts`, `.../helpers/constants.ts`: typed `appVersion` export.
- `.../components/layout/Footer.tsx`: renders `appVersion` next to the existing copyright line.
- `.github/workflows/deploy-staging.yml` / `deploy-production.yml`: `git fetch origin --tags --force`
  and `export VITE_APP_VERSION=$(git describe --tags --always)` added right after the existing
  `git reset --hard origin/<branch>` step, before the `docker compose ... build` line.
- New `CHANGELOG.md` at the repo root.
- New `apps/web/src/core/constants/releaseNotes.ts` and
  `.../components/dashboard-release/WhatsNewWidget.tsx`; wired into `DashboardPage.tsx`'s existing
  side column alongside `UpcomingEventsWidget`/`NewsPreviewWidget`.
- First tag cut under this scheme: `v1.0.0` — the app was already live in production before this;
  this establishes traceability going forward, not a new milestone.

## Not Built

- **Automatic version bumping** (e.g. semantic-release reading conventional commits) — the bump
  decision stays a manual judgment call made by whoever performs the merge, consistent with how
  merge-commit summaries are already written by hand.
- **A backend `/api/version` endpoint or assembly version.** The footer/What's New card are the only
  consumers today, both purely frontend; revisit if a backend-side consumer (e.g. a health-check
  dashboard) ever needs it.
- **What's New history browsing, per-viewer dismiss/seen-tracking, or a dedicated release-notes
  page.** The card shows only the current version; extend later if wanted.
- **A scripted/automated revert tool.** Reverting is documented as a manual process (reset the
  branch to the target tag's commit, redeploy) — no new tooling was requested or built for it.
