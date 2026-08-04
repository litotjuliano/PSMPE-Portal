# Change: Auth Rate Limiting and Account Lockout

## Status

**Ready for implementation planning.** Design approved 2026-08-04; `tasks.md` not yet written.

## Why

The portal is live at `portal.psmpe.org`, auto-deploys on every push to `main`, and holds real
member PII. It has no rate limiting of any kind, and no per-account brute-force protection.

Verified against the code and the droplet on 2026-08-04 (not assumed from the README's TODO list):

- No `AddRateLimiter` / `RequireRateLimiting` anywhere in `src/`.
- No `limit_req` or `limit_conn` anywhere in `/etc/nginx`.
- `AuthController.Login` calls `userManager.CheckPasswordAsync` directly. `SignInManager` is not
  used anywhere in the codebase, and `AddIdentity` configures only password rules — no
  `options.Lockout`. The `LockoutEnd` / `LockoutEnabled` columns exist in the schema (Identity
  created them) but nothing ever writes to them. An attacker gets unlimited password guesses
  against a known account.
- The backend cannot see client IPs at all. nginx terminates TLS and proxies to `localhost:5000`,
  but the vhost sets only `Host` — it does not `include proxy_params` and sets no
  `X-Forwarded-For` or `X-Real-IP`. The app has no `UseForwardedHeaders`. Every request reaches
  Kestrel from the Docker bridge gateway.
- nginx can be bypassed: `ufw` is inactive, `iptables -P INPUT ACCEPT`, and containers publish on
  `0.0.0.0`, so `139.59.224.32:5000` reaches the production backend directly.

The public unauthenticated surface is wider than login alone: `register`, `verify-email`,
`resend-verification-email`, `forgot-password`, `reset-password`, `username-available`, `login`.
`username-available` is a user-enumeration oracle; `forgot-password` and
`resend-verification-email` are email-send amplifiers that cost SMTP reputation, not just CPU.

## What Changes

Three mechanisms, separated by what information each can see.

**1. Rate limiter middleware** (`Microsoft.AspNetCore.RateLimiting`, built into net8.0 — no
package reference needed). Partitions on what is available without reading the request body: the
client IP.

| Policy | Endpoints | Partition key | Limit |
|---|---|---|---|
| `auth-ip` | `login`, `register`, `verify-email`, `reset-password` | client IP | 20 / 5 min |
| `auth-email-send` | `forgot-password`, `resend-verification-email` | client IP | 10 / hour |
| `username-probe` | `username-available` | client IP | 30 / min |
| `global` | everything else | client IP | 300 / min |

All four use a **fixed window** limiter — cheaper state than sliding window or token bucket (one
counter per partition, not a timestamp log), and the limits are generous enough that boundary
bursts of up to 2× the permit are not a meaningful weakness.

`username-probe` is deliberately loose: `username-available` is called from a 500ms-debounced
typeahead in `RegisterPage.tsx`, so one honest person filling in the form produces ~10–15 calls.
30/min still cuts an enumeration script down hard.

**2. ASP.NET Identity lockout** — per-account, which the rate limiter structurally cannot do.
Configure `options.Lockout` (5 failures, 15 minute lockout) and rewrite `Login` to use
`AccessFailedAsync` / `IsLockedOutAsync` / `ResetAccessFailedCountAsync`. **No migration required**
— the columns already exist and are simply unused. This is what stops distributed brute force
against a single account, where the attacker rotates IPs and per-IP limits never trigger.

**3. Per-email send throttle, in the controller.** `forgot-password` and
`resend-verification-email` must throttle on the *email address*, which means reading the request
body — and doing that inside a rate limiter partition requires buffering the body in middleware.
Cleaner as a service call in the controller: max 3 sends per address per hour, backed by
`IMemoryCache`, reusing the existing `MemoryCacheService` infrastructure.

**4. Fix the IP trust chain** — every IP-partitioned policy above is worthless without it.

nginx: add three headers to the `/api/` blocks of *both* vhosts in
`/etc/nginx/sites-available/psmpe.org` (production → `:5000`, staging → `:5001`):

```nginx
proxy_set_header X-Real-IP $remote_addr;
proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
proxy_set_header X-Forwarded-Proto $scheme;
```

Deliberately **not** `include proxy_params;` — that file sets `Host $http_host` while the vhost
sets `Host $host`. They differ when the client sends a port in the Host header, and a
rate-limiting change should not quietly alter host resolution.

Application: `app.UseForwardedHeaders(...)` as the *first* middleware, before `UseCors`,
authentication, and the limiter. Two details, both of which fail silently if wrong:

- **`KnownNetworks` must include the Docker bridge range.** The default known-proxy list is
  loopback only. nginx hits `localhost:5000`, which is `docker-proxy`, so the container sees the
  request arriving from the bridge gateway (`172.x.x.1`), not loopback. With defaults, ASP.NET
  rejects the header, discards it, and every request falls into one global bucket.
- **`ForwardLimit` stays at 1.** `$proxy_add_x_forwarded_for` *appends* the real peer address to
  whatever the client sent, so the rightmost entry is trustworthy and everything left of it is
  attacker-controlled. `ForwardLimit = 1` takes exactly that rightmost entry. Raising it would let
  an attacker choose their own partition key with a forged header — worse than no rate limiting,
  because it is rate limiting the attacker opts out of at will.

**5. Close the nginx bypass.** Change published ports in `docker-compose.yml` from `5000:8080` to
`127.0.0.1:5000:8080` (frontend likewise) so nginx becomes the only ingress. nginx proxies to
`localhost`, so this is transparent to it. Ships as a discrete step with its own verification: it
is the one change that can take the site offline if the nginx upstream and the new binding
disagree.

**6. Fail loudly.** The failure mode of a broken trust chain is silent, so two cheap guards: the
limiter logs a warning once per process if a resolved client IP falls inside the known-proxy range
(the exact fingerprint of "headers are not arriving"), and an admin-only
`GET /api/admin/diagnostics/client-ip` returns the resolved address so the chain can be confirmed
with one curl after deploy rather than inferred.

All limits are environment-configurable following the existing `Cache:*` idiom
(`GetValue<T?>("Section:Key") ?? default`): `RateLimit:Enabled` as a global kill switch plus
per-policy permit and window keys, surfaced through `docker-compose.yml`.

## Impact

- Affected specs: `auth` (**modified** — adds rate limiting, lockout, and throttling requirements)
- Affected code (indicative — see `tasks.md` when written):
  - `WebAPI`: `Program.cs` (forwarded headers, limiter registration, middleware order);
    `AuthController` (lockout flow, per-email throttle, 429/403 responses); new admin diagnostics
    endpoint
  - `Infrastructure`: `DependencyInjection.cs` (`options.Lockout`); new email-send throttle service
  - `Web`: `apiClient.ts` response interceptor gains a 429 branch
  - Infra: `docker-compose.yml` port bindings + new env vars; nginx vhost headers (droplet-side,
    not in git)
- No EF Core migration — Identity's lockout columns already exist.
- No changes to existing endpoint response shapes; this adds new rejection paths (429, and 403
  `ACCOUNT_LOCKED`) alongside them.

## Rejected Alternatives

- **nginx-only (`limit_req_zone`).** nginx only knows IP addresses. It cannot limit attempts
  against a specific account — the defense that actually matters here — and cannot see request
  bodies to throttle per email address. Limits would live in a droplet file outside git:
  unreviewable, untestable, free to drift between environments. Also bypassable today via `:5000`.
- **Defense in depth (coarse nginx + fine-grained app).** Strictly the best protection and the
  natural follow-up, but an upgrade on the app-level design rather than an alternative to it.
  Deferred until the app-side policies have proven their limits in practice.

## Gap Analysis (found while writing this proposal)

- **Identity lockout is configured nowhere but schema-ready.** `LockoutEnd`/`LockoutEnabled` exist
  in every migration snapshot; no code path writes them. Free to adopt — no migration.
- **`SignInManager` is entirely unused.** `Login` hand-rolls the password check, which is why
  lockout never engages. The rewrite is small but touches the primary auth path.
- **The frontend interceptor only handles 401.** `apiClient.ts` clears the session and redirects to
  `/login` on 401. A 429 must **not** take that path — being throttled is not being logged out.
- **`/api/ai/prompt` is excluded from this change.** It bills a real OpenAI key per call (key set in
  both production and staging `.env`), but it is unused scaffolding: the README calls it a stub, the
  controller describes itself as "starter endpoint structure", and
  `apps/web/src/core/api/endpoints/aiApi.ts` is imported by no file in the frontend. Rate limiting a
  funded endpoint with no product behind it is the wrong fix — disable it instead, under a separate
  change. Note the deferred `add-prc-ai-verification` change specs **Anthropic**, not OpenAI, so
  disabling this blocks nothing.

## Open Decisions

1. **The concrete numbers** (20/5min, 10/hour, 30/min, 300/min; 5 lockout failures / 15 min; 3
   reset emails per address per hour) are picked to be safe rather than tight, with no production
   traffic data behind them. Needs a sanity check against real member volumes.
2. **Shared NAT is an accepted trade-off, not a solved problem.** Members are a professional
   organization, so an office or chapter sharing one public IP is realistic, and per-IP limits
   punish such a group collectively. This is why IP limits are generous and the precise defenses
   live in lockout and the per-email throttle, which are per-account and unaffected by NAT.
   Tightening the IP numbers instead would produce a chapter meeting where nobody can log in.
3. **Whether the `127.0.0.1` port-binding change ships inside this change or as its own.** It is
   the piece that makes the design non-bypassable, but also the only piece that can take the site
   offline. Recommended: same change, separate step, verified independently.
4. **Lockout response code.** Recommended **403 with `code = "ACCOUNT_LOCKED"`**, mirroring the
   existing `EMAIL_NOT_CONFIRMED` shape the frontend already reads, rather than 423 Locked.

## Out of Scope

Found while investigating; real problems, but separate work:

- Production Postgres is published to the internet on `:5434` (staging on `:5433`) with no firewall.
- `portal.psmpe.org` is served with the `staging.psmpe.org` TLS certificate.
- Distributed limiter state (only needed if a second backend container is ever added; each
  environment currently runs exactly one).
- nginx-layer `limit_req` as an outer shield (the deferred defense-in-depth upgrade).
- Disabling `/api/ai/prompt` behind an `Ai:Enabled` flag (see Gap Analysis).
