# DigitalOcean Deployment

The portal runs on [DigitalOcean App Platform](https://docs.digitalocean.com/products/app-platform/)
using container images built from `src/PSMPE.Portal.WebAPI/Dockerfile` and
`apps/web/Dockerfile`, pushed to the DigitalOcean Container Registry (DOCR).

There are **two independent environments**, each its own App Platform app with its own
database and domain:

| Git branch | App spec | GitHub secret with app id | URL |
| ---------- | ---------------- | ------------------- | ------------------------- |
| `staging`  | `app.staging.yaml` | `DO_APP_ID_STAGING` | StagingPSMPE.litxus.com |
| `main`     | `app.prod.yaml`    | `DO_APP_ID_PROD`    | ProdPSMPE.litxus.com    |

Images are tagged `staging` (staging app) and `latest` (prod app); every build is also
tagged with the commit SHA for traceability.

## One-time setup

1. **Create a DOCR registry** (once, shared by both environments):
   ```
   doctl registry create <your-registry-name>
   ```
2. In both `app.staging.yaml` and `app.prod.yaml`, replace `<YOUR_DO_REGISTRY>` with that
   registry name.
3. **Create each app** and note the app id it returns:
   ```
   doctl apps create --spec infra/digitalocean/app.staging.yaml
   doctl apps create --spec infra/digitalocean/app.prod.yaml
   ```
4. **Set the SECRET-type env vars** for each app in the DO dashboard — `doctl` only creates
   the app shape, not secret values:
   `Jwt__Key`, `SEED_ADMIN_EMAIL`, `SEED_ADMIN_PASSWORD`, `OpenAI__ApiKey`.
   (`ConnectionStrings__DefaultConnection` is injected automatically from the managed DB via
   `${db.DATABASE_URL}`.)
5. **Point DNS** for each subdomain at its app (add the domain in the app's Settings, then
   create the CNAME / A record your registrar shows).
6. **Add GitHub Actions repo secrets** (Settings → Secrets and variables → Actions):
   - `DIGITALOCEAN_ACCESS_TOKEN` — a DO API token with read/write.
   - `DO_REGISTRY_NAME` — the DOCR registry name from step 1.
   - `DO_APP_ID_STAGING` — app id from step 3 (staging).
   - `DO_APP_ID_PROD` — app id from step 3 (prod).
7. **Gate production** (recommended): Settings → Environments → `production` → enable
   *Required reviewers* and add yourself. A push to `main` will then build and wait for your
   approval before it deploys.

## Subsequent deploys

Fully automated by GitHub Actions:

- Push/merge to **`staging`** → `.github/workflows/cd-staging.yml` builds + pushes images and
  runs `doctl apps update <DO_APP_ID_STAGING> --spec infra/digitalocean/app.staging.yaml`.
- Push/merge to **`main`** → `.github/workflows/cd-prod.yml` does the same for prod, after the
  manual approval on the `production` environment.

`.github/workflows/ci.yml` runs build + tests on every pull request (no deploy).

## Database

Each spec provisions a DigitalOcean-managed PostgreSQL 16 database. Staging uses a dev-tier
DB (`production: false`); prod uses `production: true` — pick an appropriately sized plan
before real traffic. The connection string is injected into the backend automatically via
`${db.DATABASE_URL}`.

## TODO

- Automate `dotnet ef database update` as a pre-deploy job instead of relying on the
  `Seed:Enabled` startup migration call (see `src/PSMPE.Portal.WebAPI/Program.cs`).
- Managed TLS is issued automatically by App Platform once each custom domain resolves.
