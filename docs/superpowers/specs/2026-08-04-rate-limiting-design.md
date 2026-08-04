# Rate limiting and auth abuse protection — design

Date: 2026-08-04
Status: approved, ready for implementation planning

## Problem

The portal is live at `portal.psmpe.org`, auto-deploys on every push to `main`, and now
holds real member PII. It has no rate limiting of any kind, and no per-account
brute-force protection.

Verified against the code and the droplet on 2026-08-04:

- No `AddRateLimiter` / `RequireRateLimiting` anywhere in `src/`.
- No `limit_req` or `limit_conn` anywhere in `/etc/nginx`.
- `AuthController.Login` calls `userManager.CheckPasswordAsync` directly. `SignInManager`
  is not used anywhere in the codebase, and `AddIdentity` configures only password rules —
  no `options.Lockout`. The `LockoutEnd` / `LockoutEnabled` columns exist in the schema
  (Identity created them) but nothing ever writes to them. An attacker gets unlimited
  password guesses against a known account.
- The backend cannot see client IPs at all. nginx terminates TLS and proxies to
  `localhost:5000`, but the vhost sets only `Host` — it does not `include proxy_params`
  and sets no `X-Forwarded-For` or `X-Real-IP`. The app has no `UseForwardedHeaders`.
  Every request reaches Kestrel from the Docker bridge gateway.
- nginx can be bypassed: `ufw` is inactive, `iptables -P INPUT ACCEPT`, and containers
  publish on `0.0.0.0`, so `139.59.224.32:5000` reaches the production backend directly.

The public unauthenticated surface is wider than login alone: `register`, `verify-email`,
`resend-verification-email`, `forgot-password`, `reset-password`, `username-available`,
`login`. `username-available` is a user-enumeration oracle;
`forgot-password` and `resend-verification-email` are email-send amplifiers that cost SMTP
reputation, not just CPU. `POST /api/ai/prompt` bills a real OpenAI key per call and
already carries a `// TODO: add per-user rate limiting` comment.

## Goal

One coherent policy set covering both credential/account abuse and AI spend. These need
different partition keys and different windows; doing only one leaves an obvious hole.

## Approach

ASP.NET Core's built-in rate limiting middleware (`Microsoft.AspNetCore.RateLimiting`,
available in net8.0 — no package reference needed).

Rejected alternatives:

- **nginx-only (`limit_req_zone`).** nginx only knows IP addresses. It cannot partition by
  authenticated user (no per-user AI quota) and cannot limit attempts against a specific
  account — the two things we most need. Limits would live in a droplet file outside git:
  unreviewable, untestable, free to drift between staging and production. Also bypassable
  today via `:5000`.
- **Defense in depth (coarse nginx + fine-grained app).** Strictly the best protection, and
  the natural follow-up, but it is an upgrade on the app-level design rather than an
  alternative to it. Deferred until the app-side policies have proven their limits in
  practice, and until the `:5000` bypass is closed (which this design does).

## Design

### 1. Three mechanisms, separated by what they can see

Rather than forcing everything through the rate limiter, responsibilities split by the
information each mechanism has access to.

**Mechanism 1 — rate limiter middleware.** Partitions on what is available without reading
the request body: client IP, or authenticated user id. Cheap, generic, runs before MVC.

| Policy | Endpoints | Partition key | Limit |
|---|---|---|---|
| `auth-ip` | `login`, `register`, `verify-email`, `reset-password` | client IP | 20 / 5 min |
| `auth-email-send` | `forgot-password`, `resend-verification-email` | client IP | 10 / hour |
| `username-probe` | `username-available` | client IP | 30 / min |
| `ai-user` | `POST /api/ai/prompt` | authenticated user id | 20 / hour |
| `global` | everything else | client IP | 300 / min |

All five policies use a **fixed window** limiter. Fixed window is chosen over sliding window
or token bucket for its cheaper state (one counter per partition rather than a timestamp log)
and because the limits above are generous enough that boundary bursts — up to 2× the permit
across a window edge — are not a meaningful weakness.

`ai-user` partitions on the authenticated user id; `AiController` is `[Authorize]`, so an
unauthenticated request is rejected by auth before the limiter matters. If the user id is
somehow absent, the policy falls back to the client IP rather than to an unlimited partition.

`username-probe` is deliberately loose: `username-available` is called from a 500ms-debounced
typeahead in `RegisterPage.tsx`, so one honest person filling in the form produces ~10–15
calls. 30/min still cuts an enumeration script down hard.

**Mechanism 2 — ASP.NET Identity lockout.** Per-account, which the rate limiter structurally
cannot do. Configure `options.Lockout` (5 failures, 15 minute lockout) and rewrite `Login`
to use `AccessFailedAsync` / `IsLockedOutAsync` / `ResetAccessFailedCountAsync`.
**No migration required** — the columns already exist and are simply unused. This is what
stops distributed brute force against a single account, where the attacker rotates IPs and
per-IP limits never trigger.

**Mechanism 3 — per-email send throttle, in the controller.** `forgot-password` and
`resend-verification-email` must throttle on the *email address*, which means reading the
request body — and doing that inside a rate limiter partition requires buffering the body in
middleware. Cleaner as a small service call in the controller: max 3 sends per address per
hour, backed by `IMemoryCache`, reusing the caching infrastructure already in place
(`MemoryCacheService`).

All limits are environment-configurable following the existing `Cache:*` idiom
(`configuration.GetValue<T?>("Section:Key") ?? default`): `RateLimit:Enabled` as a global
kill switch, plus per-policy permit and window keys, surfaced through `docker-compose.yml`
alongside the existing settings.

**Accepted trade-off.** Members are a professional organization, so an office or campus
sharing one public IP is realistic, and per-IP limits punish such a group collectively. This
is why the IP limits above are generous and the precise defenses live in Mechanisms 2 and 3,
which are per-account and unaffected by shared NAT. Tightening the IP numbers instead would
produce a chapter meeting where nobody can log in.

### 2. The IP trust chain

Every IP-partitioned policy above is worthless unless this works, and it currently does not.

**nginx.** Add three headers to the `/api/` blocks of *both* vhosts in
`/etc/nginx/sites-available/psmpe.org` (production → `:5000`, staging → `:5001`):

```nginx
proxy_set_header X-Real-IP $remote_addr;
proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
proxy_set_header X-Forwarded-Proto $scheme;
```

Deliberately **not** `include proxy_params;` — that file sets `Host $http_host` while the
vhost currently sets `Host $host`. They differ when the client sends a port in the Host
header, and a rate-limiting change should not quietly alter host resolution. Add three
explicit lines; leave the existing `Host` line alone.

**Application.** `app.UseForwardedHeaders(...)` as the *first* middleware in the pipeline,
before `UseCors`, authentication, and the limiter. Two details, both of which fail silently
if wrong:

- **`KnownProxies`/`KnownNetworks` must include the Docker bridge gateway.** The default
  known-proxy list is loopback only. nginx hits `localhost:5000`, which is `docker-proxy`, so
  the container sees the request arriving from the bridge gateway (`172.x.x.1`), not
  loopback. With defaults, ASP.NET rejects the header, discards it, and every request falls
  into one global bucket.
- **`ForwardLimit` stays at 1.** `$proxy_add_x_forwarded_for` *appends* the real peer address
  to whatever the client sent, so the rightmost entry is the trustworthy one and everything to
  its left is attacker-controlled. `ForwardLimit = 1` takes exactly that rightmost entry.
  Raising it would let an attacker pick their own partition key with a forged header — worse
  than no rate limiting, because it is rate limiting the attacker opts out of at will.

**Fail loudly.** The failure mode here is silent, so two cheap guards:

- The limiter logs a warning, once per process, if a resolved client IP falls inside the
  known-proxy range. That is the exact fingerprint of "headers are not arriving," and it is
  the difference between noticing in the logs and noticing when a member reports that the
  whole site is throttled.
- An admin-only `GET /api/admin/diagnostics/client-ip` returning the resolved address, so the
  chain can be confirmed end to end with one curl after deploy rather than inferred.

**Close the bypass.** Change the published ports in `docker-compose.yml` from `5000:8080` to
`127.0.0.1:5000:8080` (and the frontend likewise) so nginx becomes the only ingress. nginx
proxies to `localhost`, so this is transparent to it. Ships as a discrete step with its own
verification: it is the one change that can take the site off the internet if the nginx
upstream and the new binding disagree.

### 3. The 429 contract

- Status **429**, body as `ProblemDetails` with `application/problem+json`, matching
  `ExceptionHandlingMiddleware` so the API has one error shape rather than two.
- **`Retry-After`** header whenever the limiter supplies `MetadataName.RetryAfter`. Without
  it the frontend can only say "try later," which invites an immediate retry.
- **Account lockout returns 403** with `code = "ACCOUNT_LOCKED"`, mirroring the existing
  `EMAIL_NOT_CONFIRMED` response shape in `AuthController`; the frontend already reads that
  pattern.
- **Frontend:** extend the existing response interceptor in `apps/web/src/core/api/apiClient.ts`,
  which today handles only 401. Add a 429 branch surfacing the wait time. It must **not**
  clear the session the way the 401 path does — being throttled is not being logged out.
- `forgot-password` keeps returning its existing generic response even when throttled, so the
  throttle does not become the account-enumeration oracle the endpoint otherwise avoids.

### 4. Testing

Integration tests through the existing `WebApplicationFactory` setup in
`PSMPE.Portal.WebAPI.IntegrationTests`:

- Exceeding `auth-ip` returns 429, with `Retry-After` present and a `ProblemDetails` body.
- 5 bad passwords, then the 6th returns 403 `ACCOUNT_LOCKED`; a successful login resets the
  failure count.
- The 4th `forgot-password` for one address within the hour is throttled, while a *different*
  address is unaffected — proving the partition key is the email, not a global counter.
- A forged `X-Forwarded-For` originating outside the known-proxy range is ignored.

**Test isolation constraint.** Limiter state is process-wide and in-memory, and
`WebApplicationFactory` shares one host across a test class, so tests will pollute each
other's counters and fail in order-dependent ways. Each test sends a **distinct
`X-Forwarded-For`**, giving it its own partition. This also exercises the Section 2 trust
chain in every test rather than mocking it away.

### 5. Rollout

1. Staging first.
2. Curl the diagnostics endpoint to confirm the real client IP resolves.
3. Exercise the limits by hand.
4. Production.
5. The port-binding change ships as its own step, verified independently.

## Out of scope

Found while investigating; real problems, but separate work:

- Production Postgres is published to the internet on `:5434` (staging on `:5433`) with no
  firewall.
- `portal.psmpe.org` is served with the `staging.psmpe.org` TLS certificate.
- Distributed limiter state (only needed if a second backend container is ever added; each
  environment currently runs exactly one).
- nginx-layer `limit_req` as an outer shield (the deferred defense-in-depth upgrade).
