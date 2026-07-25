# Tasks: github-actions-domain-update

## 1. Branch rename (GitHub)

- [x] 1.1 Rename `uat` → `staging` on GitHub via native rename API (done by repo
      owner; verified via `git fetch` — `origin/uat` gone, `origin/staging` present)
- [x] 1.2 Realign local git refs to match (local `uat` → `staging`, tracking
      `origin/staging`; old unrelated local `staging` branch preserved as
      `staging-legacy-do-app-platform`)

## 2. Workflow files

- [x] 2.1 Rename `.github/workflows/deploy-uat.yml` → `deploy-staging.yml`
- [x] 2.2 Update workflow `name`, header comment, `on.push.branches`,
      `concurrency.group`, `environment`, and SSH script branch references from
      `uat` to `staging`
- [x] 2.3 Confirm `ci.yml` and `deploy-production.yml` need no changes (verified in
      proposal.md Impact section)

## 3. Docs

- [x] 3.1 Update `README.md`: branch references (`uat` → `staging`), workflow
      filename references, domain references (`uatpsmpe.litxus.com` →
      `staging.psmpe.org`, `prodpsmpe.litxus.com` → `portal.psmpe.org`)
- [x] 3.2 Update `BRANCHING.md`: branch and workflow filename references

## 4. Review & handoff

- [x] 4.1 Commit on `feature/domain-migration-psmpe-org` (branched from `main`)
- [ ] 4.2 Present before/after diff to repo owner for review
- [ ] 4.3 Hand off GitHub Settings checklist (Environment rename, branch protection
      verification) — manual, no `gh` auth available this session
- [ ] 4.4 Get explicit approval before pushing to GitHub / opening a PR
