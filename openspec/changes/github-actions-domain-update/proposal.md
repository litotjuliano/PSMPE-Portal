# Change: GitHub Actions Domain Update (litxus.com → psmpe.org)

## Why

PSMPE Portal is moving off the `litxus.com` subdomains it was deployed under during
early development (`uatpsmpe.litxus.com`, `prodpsmpe.litxus.com`) onto its own
domain, `psmpe.org`. Alongside the domain move, the `uat` branch is being renamed
`staging` to match conventional naming and the new `staging.psmpe.org` host.

The droplet-side infrastructure (nginx routing, Let's Encrypt SSL for
`staging.psmpe.org` / `portal.psmpe.org`) and the GitHub-side branch rename
(`uat` → `staging`, via the GitHub API's native branch-rename endpoint, which
carries over branch protection rules and open PRs automatically) were completed
directly by the repo owner before this change. What remains is bringing the
repo's own workflow files and docs in line with the renamed branch and new
domains.

## What Changes

- `.github/workflows/deploy-uat.yml` is renamed to `deploy-staging.yml` and updated
  to trigger on `staging` instead of `uat` (workflow name, push trigger, concurrency
  group, GitHub Environment reference, and the SSH deploy script's `git fetch` /
  `checkout` / `reset --hard` branch references).
- `README.md` and `BRANCHING.md` are updated to reference the `staging` branch,
  `deploy-staging.yml`, `staging.psmpe.org`, and `portal.psmpe.org` instead of their
  `uat`/`litxus.com` predecessors.
- No deployment logic, branch promotion model, approval gates, or infrastructure
  configuration changes — this is a rename/reference update only.

## Impact

- Affected files:
  - `.github/workflows/deploy-uat.yml` → `.github/workflows/deploy-staging.yml`
    (renamed + edited)
  - `README.md` (domain/branch references)
  - `BRANCHING.md` (branch reference)
- Not affected (verified, not assumed):
  - `.github/workflows/ci.yml` — already triggers on `staging` (leftover from
    before the `uat` branch existed; never cleaned up), no domain references at all
    since it never deploys.
  - `.github/workflows/deploy-production.yml` — no domain references; `main` branch
    trigger is unchanged.
  - Droplet nginx config (`/etc/nginx/sites-available/psmpe.org`) and SSL certs —
    already live and correct, out of scope per "don't touch infra configs."
  - Docker Compose project naming (`-p psmpe-uat` → `-p psmpe-staging`) — initially
    left unchanged as infra, then explicitly renamed at the repo owner's request for
    full consistency (see Decision 2, superseding the original call).

## Decisions

1. **`STAGING_DOMAIN`/`PRODUCTION_DOMAIN` GitHub Actions variables are not
   introduced.** The original task brief assumed these exist; they don't — domains
   are only referenced in the droplet's nginx config, not parameterized through any
   workflow. Adding them now would be new deployment logic, which the task
   explicitly excludes. Flagged for the repo owner instead of silently added.
2. **Docker Compose project renamed `psmpe-uat` → `psmpe-staging` (supersedes the
   original "leave unchanged" call).** The repo owner asked for full consistency
   rather than a mixed `staging` branch / `uat`-named infra state. Done as a
   data-preserving migration on the droplet: containers stopped, `Postgres`/uploads
   volumes copied byte-for-byte to new `psmpe-staging_*` volumes (verified via a
   temporary Postgres container booting clean — "shut down" not "crash recovery" —
   with all expected tables and matching data), the checkout directory renamed
   `/opt/psmpe-portal/uat` → `/opt/psmpe-portal/staging`, the new stack brought up
   and verified end-to-end (`staging.psmpe.org` → nginx → new container → migrated
   DB), old containers/volumes/images removed only after verification passed.
   `DEPLOY_PATH` for the `staging` GitHub Environment must be
   `/opt/psmpe-portal/staging` (not `/opt/psmpe-portal/uat`).
3. **GitHub Environment rename (`uat` → `staging`) and branch protection
   verification are manual steps for the repo owner**, not automated here — no
   authenticated `gh`/API access was available in this session to perform them
   safely. Also discovered along the way: the deploy SSH user is `deploy` (uid 1000,
   `docker` group), not `root` — the checkout directory is owned by `deploy`, and
   `root` hits git's "dubious ownership" guard against it.
