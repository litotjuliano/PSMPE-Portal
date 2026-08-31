# Tasks: add-release-versioning

**Goal:** Every merge to `staging`/`main` gets a meaningful, traceable version — visible in the
running app's footer, recorded in `CHANGELOG.md`, mirrored in an in-app "What's New" card, and
named in the merge commit message itself.

**Architecture:** Git tags as the sole source of truth (no committed VERSION file); a Docker build
`ARG`/`ENV` pair (mirroring the existing `VITE_API_BASE_URL` pattern) carries the tag into the
frontend bundle; the SSH deploy scripts compute it via `git describe` on whatever commit they just
checked out.

**Tech Stack:** Same as the rest of the repo — Vite/React frontend, .NET 8 backend (untouched by
this change), GitHub Actions SSH-deploy to a single Docker Compose droplet.

---

## 1. Versioning mechanism

- [x] `docker-compose.yml`: `frontend.build.args` gains `VITE_APP_VERSION: ${VITE_APP_VERSION:-dev}`.
- [x] `apps/web/Dockerfile`: `ARG VITE_APP_VERSION=dev` / `ENV VITE_APP_VERSION=$VITE_APP_VERSION`,
      same spot as the existing `VITE_API_BASE_URL` pair.
- [x] `apps/web/src/vite-env.d.ts`: `VITE_APP_VERSION` added to `ImportMetaEnv`.
- [x] `apps/web/src/integrations/template/helpers/constants.ts`: `appVersion` export, falling back
      to `'dev'` if the env var is empty.
- [x] `.github/workflows/deploy-staging.yml` / `deploy-production.yml`: `git fetch origin --tags --force`
      + `export VITE_APP_VERSION=$(git describe --tags --always)` added right after
      `git reset --hard origin/<branch>`, before `docker compose ... build`.

## 2. Footer

- [x] `Footer.tsx`: renders `appVersion` next to the existing `{currentYear} © {appName}` line.

## 3. CHANGELOG.md + What's New card

- [x] New `CHANGELOG.md` at repo root, Keep a Changelog format, `[Unreleased]` + a `[1.0.0]` section
      for today's actual shipped work (the membership-access revert/re-fix arc, the mid-cycle Portal
      Access payment feature, the Super Admin permission fix, the MEMBERSHIP_NOT_STARTED security
      fix — everything already on `develop`/`staging`/`main` from today's session).
- [x] New `apps/web/src/core/constants/releaseNotes.ts` — structured mirror of the same `[1.0.0]`
      entry, hand-authored in the same commit (deviated from the plan's originally-suggested
      `core/data/` path to `core/constants/`, matching the existing directory that already holds
      small static-data files like `uploadLimits.ts`).
- [x] New `apps/web/src/integrations/template/components/dashboard-release/WhatsNewWidget.tsx` —
      matches `appVersion` (stripping any `-rc.N` suffix) against `releaseNotes`, renders that
      entry's bullet list, renders nothing if no match (e.g. local dev's `'dev'`).
- [x] `DashboardPage.tsx`: `WhatsNewWidget` added to the existing side column, above
      `UpcomingEventsWidget`/`NewsPreviewWidget`, no role gate — same visibility as those two.

## 4. Documentation

- [x] `openspec/changes/add-release-versioning/proposal.md` + this file.

## 5. Cutting v1.0.0 (the actual bump)

- [ ] Commit everything above to `develop`, push.
- [ ] Merge `develop` → `staging` via the established worktree pattern, using
      `git merge origin/develop -m "Merge develop into staging: v1.0.0-rc.1 — <summary>"` instead of
      `--no-edit`. Tag `v1.0.0-rc.1` (annotated) on that merge commit. Verify `dotnet test` +
      `npm run build` on the merged result before pushing branch + tag.
- [ ] Merge `staging` → `main` the same way:
      `-m "Merge staging into main: v1.0.0 — <summary>"`, tag `v1.0.0` on that commit. Verify tests +
      build again before pushing branch + tag.
- [ ] Clean up worktrees/temp branches per the established pattern.

## 6. Verification

- [ ] `dotnet test src/PSMPE.Portal.sln` and `npm run build`/`npm run lint` in `apps/web` — on
      `develop` before merging, and again on each merged result before pushing.
- [ ] `git fetch --tags && git describe --tags <sha>` locally against the pushed `staging`/`main`
      commits resolves to `v1.0.0-rc.1`/`v1.0.0` respectively.
- [ ] After the droplet deploys run: `staging.psmpe.org`'s footer shows `v1.0.0-rc.1`,
      `portal.psmpe.org`'s shows `v1.0.0`; the Dashboard's What's New card shows the v1.0.0 notes on
      both.
- [ ] `git log --oneline` on `staging`/`main` reads as a version history via the new merge commit
      messages.
