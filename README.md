# PSMPE Portal

An enterprise Portal/CMS starter: a .NET 8 Clean Architecture backend (ASP.NET Core
Identity + JWT auth, role- and ownership-based authorization, PostgreSQL/EF Core, an
OpenAI SDK stub) paired with a React + Vite + TypeScript + Tailwind CSS frontend,
Dockerized for local dev, and wired for CI/CD to DigitalOcean App Platform.

This is a **starter**, not a finished product — advanced/optional features are marked
`// TODO:` in code and called out below rather than implemented speculatively.

## Overview

- **Backend**: `src/PSMPE.Portal.*` — Clean Architecture (Domain → Application →
  Infrastructure → WebAPI), ASP.NET Core Identity + JWT bearer auth, three fixed roles
  (`Super Admin`, `Admin`, `Content Creator`), ownership-based CMS rules, Swagger with
  JWT support, an OpenAI SDK-backed prompt execution endpoint.
- **Frontend**: `apps/web` — React 19 + Vite + TypeScript + Tailwind CSS v4 + Preline
  UI, integrated with the licensed Tailwick admin dashboard template
  (`src/integrations/template/`) for layout, dashboard, login, and CMS page styling.
- **Docs**: `openspecs/` — lightweight per-feature API/contract notes.
- **Infra**: `docker-compose.yml` for local dev, `infra/digitalocean/` for App Platform
  deployment, `.github/workflows/` for CI/CD.

## Folder structure

```
PSMPE Portal/
├── src/                          # Backend (Clean Architecture)
│   ├── PSMPE.Portal.Domain/          # Entities, enums — no dependencies
│   ├── PSMPE.Portal.Application/     # Business logic, interfaces, DTOs
│   ├── PSMPE.Portal.Infrastructure/  # EF Core, Identity, JWT, OpenAI, auth handlers
│   └── PSMPE.Portal.WebAPI/          # Controllers, Program.cs, Swagger, Dockerfile
├── tests/
│   ├── PSMPE.Portal.Application.UnitTests/
│   └── PSMPE.Portal.WebAPI.IntegrationTests/
├── apps/web/                     # Frontend (Vite + React + TS)
│   └── src/
│       ├── core/                     # Auth, API client, CMS pages (data-fetching)
│       └── integrations/template/    # Licensed Tailwick template — layout, dashboard, styling
├── openspecs/                    # Per-feature API/contract docs
├── infra/digitalocean/           # App Platform spec + deployment notes
├── .github/workflows/            # ci.yml, cd.yml
├── docker-compose.yml
└── .env.example
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js ≥20.19 or ≥22.12](https://nodejs.org/) + npm (Vite 7 requires this; Node 20.15 and earlier will fail to install)
- [Docker](https://www.docker.com/) and Docker Compose (for the all-in-one local setup)
- PostgreSQL 16 (only if you want to run it outside Docker)

## Quick start (Docker Compose)

```bash
cp .env.example .env
# edit .env — at minimum set a real Jwt__Key and SEED_ADMIN_PASSWORD

docker compose up --build
```

This starts Postgres, the backend (`http://localhost:5000`, Swagger at
`http://localhost:5000/swagger`), and the frontend (`http://localhost:5173`). On first
boot the backend applies EF Core migrations and seeds roles, a default Super Admin
account, and starter system configuration (see `Seed:Enabled` below).

## Running locally without Docker

### Backend

```bash
cd src
dotnet restore
dotnet user-secrets set "Jwt:Key" "a-long-random-development-secret" --project PSMPE.Portal.WebAPI
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=psmpe_portal;Username=psmpe_user;Password=change-me-locally" --project PSMPE.Portal.WebAPI
dotnet run --project PSMPE.Portal.WebAPI
```

(Requires a locally running Postgres — e.g. `docker compose up postgres`.)

### Frontend

```bash
cd apps/web
cp .env.example .env
npm install
npm run dev
```

Vite serves the app at `http://localhost:5173` and proxies API calls to
`VITE_API_BASE_URL` (default `http://localhost:5000`).

## PostgreSQL configuration

The backend connects via the standard Npgsql connection string format, read from
`ConnectionStrings:DefaultConnection` (env var form: `ConnectionStrings__DefaultConnection`):

```
Host=<host>;Port=5432;Database=psmpe_portal;Username=<user>;Password=<password>
```

In Docker Compose, `postgres` is the hostname (the service name). Outside Docker, use
`localhost` and whatever port you've exposed (default `5432`).

## Environment variables

See `.env.example` (root, used by Docker Compose) and `apps/web/.env.example` (frontend).
Key variables:

| Variable | Purpose |
|---|---|
| `ConnectionStrings__DefaultConnection` | Postgres connection string |
| `Jwt__Key` / `Jwt__Issuer` / `Jwt__Audience` / `Jwt__ExpiryMinutes` | JWT signing config |
| `SEED_ADMIN_EMAIL` / `SEED_ADMIN_PASSWORD` | Default Super Admin created on first run |
| `Seed__Enabled` | Whether to run migrations + seeding on startup |
| `OpenAI__ApiKey` / `OpenAI__Model` | OpenAI SDK configuration for `/api/ai/prompt` |
| `VITE_API_BASE_URL` | Frontend → backend base URL |

**Never commit a real `.env` file** — it's git-ignored; only `.env.example` is tracked.

## Migrations and seeding

Migrations live in `src/PSMPE.Portal.Infrastructure/Persistence/Migrations`. To add a
new one after changing entities:

```bash
dotnet tool install --global dotnet-ef   # first time only
dotnet ef migrations add <Name> \
  --project src/PSMPE.Portal.Infrastructure/PSMPE.Portal.Infrastructure.csproj \
  --startup-project src/PSMPE.Portal.WebAPI/PSMPE.Portal.WebAPI.csproj \
  --output-dir Persistence/Migrations
```

When `Seed:Enabled` is `true` (the default in Docker Compose and `appsettings.Development.json`),
`Program.cs` automatically calls `dotnet ef database update`'s runtime equivalent
(`Database.MigrateAsync()`) and seeds:

- The three fixed roles: `Super Admin`, `Admin`, `Content Creator`.
- A default Super Admin account from `SEED_ADMIN_EMAIL` / `SEED_ADMIN_PASSWORD`.
- Starter `SystemConfig` rows and one system `Layout` (used to demonstrate the
  "system layouts can't be deleted except by a Super Admin" rule).

Seeding is idempotent — safe to run on every startup.

## Authentication & authorization at a glance

- JWT bearer auth via ASP.NET Core Identity (`POST /api/auth/register`, `POST /api/auth/login`).
- Roles: `Super Admin`, `Admin`, `Content Creator` — new self-registrations always get
  `Content Creator`; higher roles are granted via `POST /api/admin/users/{id}/roles`
  (Super Admin only).
- Ownership rule: a user can edit/delete their own `ContentItem`s and `Layout`s; an
  `Admin`/`Super Admin` can manage anyone's. System layouts (`IsSystemLayout == true`)
  can only be deleted by a `Super Admin`.
- See `openspecs/` for the per-endpoint contract and `PSMPE.Portal.Infrastructure/Authorization/`
  for the actual handlers.

## API docs

Swagger UI is available at `/swagger` when running the backend (enabled in Development;
see `Program.cs` to enable it elsewhere). It's configured with a Bearer auth scheme —
log in via `/api/auth/login`, paste the returned token into the "Authorize" button, and
you can exercise every endpoint independently of the frontend.

## Testing

```bash
dotnet test src/PSMPE.Portal.sln
```

Covers JWT-issuing auth flows (integration, via `WebApplicationFactory` + EF Core
InMemory), ownership authorization logic (unit), and content/layout ownership rules
(unit). Frontend: `npm run lint && npm run build` in `apps/web`.

## Commercial template integration

The frontend runs on the licensed **Tailwick** admin dashboard template (Envato Market,
`React-TS` distribution) — Tailwind CSS v4, Preline UI, ApexCharts. Template code/assets
live at `apps/web/src/integrations/template/`; `core/` pages consume it through a single
barrel (`index.ts`). See
[`apps/web/src/integrations/template/README.md`](apps/web/src/integrations/template/README.md)
for what's ported vs. still available-but-unused in the licensed package, and how to
port more of it later.

## Deployment

CI (`.github/workflows/ci.yml`) builds/tests both stacks on every PR and push to
`main`/`develop`. CD (`.github/workflows/cd.yml`) builds and pushes Docker images to
DigitalOcean Container Registry and deploys to App Platform on push to `main` — see
[`infra/digitalocean/README.md`](infra/digitalocean/README.md) for one-time setup
(registry, app creation, required secrets).

## Branching

See [`BRANCHING.md`](BRANCHING.md) — `main` / `develop` / `feature/*`.

## Known TODOs (by design — not oversights)

- Refresh token rotation (JWT access tokens only, short-lived).
- API rate limiting / throttling, especially on `/api/ai/prompt`.
- Multi-tenancy.
- OpenAI streaming responses (single-shot completion only).
- Most of the Tailwick template package is unported (HR, invoicing, ecommerce catalog,
  chat, mailbox, calendar, other auth styles/flows, landing pages) — see
  `apps/web/src/integrations/template/README.md` for the full list.
- Frontend bundle isn't code-split yet (~1.1MB main chunk, mostly ApexCharts/Preline/
  icon libraries) — fine for a starter, worth revisiting with route-based lazy-loading
  before this grows much further.
- Full audit logging / soft-delete.
- Identity email confirmation / password reset flows.
- Automated `dotnet ef database update` as a distinct CD step (currently runs at
  backend startup, gated by `Seed:Enabled`).
- Fine-grained permissions beyond the three fixed roles.
