# DigitalOcean Deployment

This starter deploys to [DigitalOcean App Platform](https://docs.digitalocean.com/products/app-platform/)
using container images built from the `src/PSMPE.Portal.WebAPI/Dockerfile` and
`apps/web/Dockerfile`, pushed to DigitalOcean Container Registry (DOCR).

## One-time setup

1. Create a DOCR registry: `doctl registry create <your-registry-name>`
2. Create the app the first time from `app.yaml`:
   ```
   doctl apps create --spec infra/digitalocean/app.yaml
   ```
   Note the returned app ID — you'll need it for the `DO_APP_ID` GitHub secret.
3. In the DO dashboard, set the `SECRET`-type environment variables listed in `app.yaml`
   (`Jwt__Key`, `SEED_ADMIN_EMAIL`, `SEED_ADMIN_PASSWORD`, `OpenAI__ApiKey`) — `doctl`
   only creates the app shape, not secret values.
4. Add these GitHub Actions repo secrets (used by `.github/workflows/cd.yml`):
   - `DIGITALOCEAN_ACCESS_TOKEN`
   - `DO_REGISTRY_NAME`
   - `DO_APP_ID`

## Subsequent deploys

Handled automatically by `.github/workflows/cd.yml` on every push to `main`: it builds
both images, pushes them to DOCR, and runs
`doctl apps update <DO_APP_ID> --spec infra/digitalocean/app.yaml`.

## Database

`app.yaml` provisions a DigitalOcean-managed PostgreSQL database (`production: false` by
default — a dev-tier database). Its connection string is injected into the backend
service automatically via `${db.DATABASE_URL}`. Switch `production: true` and pick a
suitable plan before using this for real traffic.

## TODO

- Automate `dotnet ef database update` as a pre-deploy job instead of relying on
  `Seed:Enabled`'s startup migration call (see `src/PSMPE.Portal.WebAPI/Program.cs`).
- Add a custom domain + managed TLS once one is available.
