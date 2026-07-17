# Branching Strategy

This project uses a simple three-tier branching model.

## Branches

- **`main`** — Production. Always deployable. Protected; only receives merges from `develop` (via PR) or hotfixes.
- **`develop`** — Integration branch. Latest accepted feature work lands here before being promoted to `main`.
- **`feature/*`** — Short-lived branches for individual features or fixes, branched from `develop`.

## Workflow

1. Branch off `develop`: `git checkout -b feature/short-description develop`
2. Commit and push your work, open a PR into `develop`.
3. CI (`.github/workflows/ci.yml`) must pass before merging.
4. Periodically, `develop` is merged into `main` via a release PR. Pushing to `main` triggers
   `.github/workflows/deploy-production.yml`, which deploys to the production droplet over SSH
   (`uat` deploys the same way via `deploy-uat.yml`).

## Hotfixes

For urgent production fixes, branch `hotfix/*` from `main`, fix, PR back into `main`, then merge `main` back into `develop` to keep branches in sync.
