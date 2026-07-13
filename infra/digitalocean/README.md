# DigitalOcean Deployment

The portal runs on [DigitalOcean App Platform](https://docs.digitalocean.com/products/app-platform/).
App Platform builds the container images **directly from this GitHub repo** (using
`src/PSMPE.Portal.WebAPI/Dockerfile` and `apps/web/Dockerfile`) — there is no container
registry, no `doctl`, and no GitHub Actions deploy step.

Two independent environments, each its own App Platform app, database, and domain:

| Git branch | App spec | Deploys | URL |
| ---------- | ---------------- | ------------------------- | ----------------------- |
| `staging`  | `app.staging.yaml` | automatically on every push | StagingPSMPE.litxus.com |
| `main`     | `app.prod.yaml`    | **manually** (one click in DO) | ProdPSMPE.litxus.com  |

Production is set to **not** auto-deploy (`deploy_on_push: false`), so shipping to prod is a
deliberate click — that click is your production gate.

## One-time setup (per app)

Do this once for staging, once for prod:

1. DO dashboard → **Create App** → **GitHub** → authorize DigitalOcean to access the
   `litotjuliano/PSMPE-Portal` repo when prompted (one-time OAuth).
2. Pick the repo and the matching branch (`staging` or `main`). App Platform detects the
   two Dockerfiles.
3. Open the app's **Settings → App Spec (YAML)** and paste the contents of
   `app.staging.yaml` (or `app.prod.yaml`) so the routes, health check, env vars, domain,
   and database match exactly. Save.
   - Alternatively, if you install `doctl`: `doctl apps create --spec infra/digitalocean/app.staging.yaml`.
4. Set the **SECRET-type env vars** in the DO dashboard (the spec declares them but can't
   hold their values): `Jwt__Key`, `SEED_ADMIN_EMAIL`, `SEED_ADMIN_PASSWORD`,
   `OpenAI__ApiKey`. (`ConnectionStrings__DefaultConnection` is injected automatically from
   the managed DB via `${db.DATABASE_URL}`.)
5. **DNS:** add the domain (StagingPSMPE / ProdPSMPE .litxus.com) under the app's Settings,
   then create the CNAME record DO shows you. Managed TLS is issued automatically once it
   resolves.

## Day-to-day deploys

- **Staging:** push/merge to the `staging` branch → App Platform rebuilds and redeploys
  automatically. Nothing else to do.
- **Production:** merge to `main`, then in the DO dashboard open the prod app and click
  **Deploy** (Create Deployment). It builds the latest `main` and goes live.

`.github/workflows/ci.yml` still runs build + tests on every pull request (a safety check
before merge). It does not deploy.

## Not needed anymore

The old container-registry approach is gone, so these GitHub secrets are unused and can be
deleted: `DIGITALOCEAN_ACCESS_TOKEN`, `DO_REGISTRY_NAME`, `DO_APP_ID_STAGING`,
`DO_APP_ID_PROD`.

## Database

Each spec provisions a DigitalOcean-managed PostgreSQL 16 database (staging dev-tier, prod
`production: true` — size it before real traffic). The connection string is injected into
the backend automatically via `${db.DATABASE_URL}`.

## TODO

- Automate `dotnet ef database update` as a pre-deploy job instead of relying on the
  `Seed:Enabled` startup migration call (see `src/PSMPE.Portal.WebAPI/Program.cs`).
