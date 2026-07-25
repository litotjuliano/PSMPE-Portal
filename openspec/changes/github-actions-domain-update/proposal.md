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
  - Docker Compose project naming (`-p psmpe-uat` in the deploy script) — left as
    infrastructure identifier, not a branch-name reference; renaming it would
    require re-provisioning containers/volumes on the droplet, which is out of
    scope and carries data risk.

## Decisions

1. **`STAGING_DOMAIN`/`PRODUCTION_DOMAIN` GitHub Actions variables are not
   introduced.** The original task brief assumed these exist; they don't — domains
   are only referenced in the droplet's nginx config, not parameterized through any
   workflow. Adding them now would be new deployment logic, which the task
   explicitly excludes. Flagged for the repo owner instead of silently added.
2. **Docker Compose project name kept as `psmpe-uat`.** Renaming it to
   `psmpe-staging` would mean tearing down and recreating the running containers
   (including the Postgres volume) on the droplet — an infrastructure change with
   data risk, out of scope for a "domain references and branch names" update.
3. **GitHub Environment rename (`uat` → `staging`) and branch protection
   verification are manual steps for the repo owner**, not automated here — no
   authenticated `gh`/API access was available in this session to perform them
   safely.
