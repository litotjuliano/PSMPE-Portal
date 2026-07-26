# Caching Strategy

## Purpose

The backend had no caching at all before this change - every read hit Postgres, including reads
of data that almost never changes. This adds an in-memory cache (`IMemoryCache`) for exactly the
reads that are both **frequently queried** and **rarely changing**, with correct invalidation on
every mutation that could make a cached value stale.

## What's cached, and why

| Data | Service / method | Cache key(s) | Default duration | Invalidated on |
|---|---|---|---|---|
| CMS content items (all) | `ContentService.GetAllAsync` | `content:all` | 300s | `CreateAsync`, `UpdateAsync`, `DeleteAsync` |
| CMS content item (single) | `ContentService.GetByIdAsync` | `content:{id}` | 300s | `UpdateAsync`, `DeleteAsync` (for that `id`) |
| CMS layouts (all) | `LayoutService.GetAllAsync` | `layout:all` | 300s | `CreateAsync`, `DeleteAsync` |
| Membership grace-period days | `MemberService.GetGracePeriodDaysAsync` (private, feeds `GetAllAsync`/`GetByIdAsync`/`GetByUserIdAsync`) | `config:membership-grace-period-days` | 600s | *(none - see below)* |

Content and layouts are read by every authenticated user on effectively every page load, and
written only by the owning author or an Admin/Super Admin - a textbook read-heavy/write-light
profile. The membership grace-period value is a single `SystemConfigs` row (key
`MembershipGracePeriodDays`) that is queried on **every** member lookup but has **no write path
anywhere in the codebase** - it's seeded once at startup and never updated by any endpoint, so
it's effectively a static value paying a Postgres round-trip on every request for no reason.

## What's deliberately NOT cached

`Member` records, uploads, certificates, and PRC verification data (`PrcVerificationHistory`,
`PendingPrcLicenseNo`, `PrcIdVerified`, etc.) are **not cached**, on purpose. These mutate
constantly - profile edits, admin approve/reject decisions - and this application's core value is
the correctness of that workflow. Caching them risks the exact kind of staleness bug (an admin
approves a PRC change and the member or another admin still sees "pending" for several minutes)
that would undermine trust in a verification-critical flow. If a future change wants to cache any
of this, it needs its own careful invalidation design reviewed on its own merits - it should not
be added casually just because the infrastructure below makes it easy to.

## Design

### `ICacheService` (`src/PSMPE.Portal.Application/Common/Interfaces/ICacheService.cs`)

```csharp
public interface ICacheService
{
    Task<T> GetOrCreateAsync<T>(string key, string durationConfigKey, int defaultDurationSeconds, Func<Task<T>> factory);
    void Remove(string key);
}
```

An abstraction over "how caching actually happens," mirroring the existing
`IFileStorageService`/`IEmailSender` pattern (interface in Application, real implementation in
Infrastructure). `durationConfigKey` lets each call site name its own configurable duration
(e.g. `"Cache:ContentDurationSeconds"`) without every Application service needing its own
`IConfiguration` dependency - `MemoryCacheService` (below) is the only place that reads
`IConfiguration` directly.

### `MemoryCacheService` (`src/PSMPE.Portal.Infrastructure/Services/MemoryCacheService.cs`)

The real, `IMemoryCache`-backed implementation. Registered as a **singleton** in
`DependencyInjection.AddInfrastructure` (matching `IMemoryCache`'s own process-wide lifetime, and
the one other singleton in the DI graph, `IDateTimeProvider`):

```csharp
services.AddMemoryCache();
services.AddSingleton<ICacheService, MemoryCacheService>();
```

Reads `Cache:Enabled` once at construction (default `true`) as a global kill switch - if `false`,
every call falls through straight to the factory, reproducing today's uncached behavior exactly.
Useful for ruling out a caching bug in production without a redeploy.

### `NoOpCacheService` (`src/PSMPE.Portal.Application/Common/Caching/NoOpCacheService.cs`) and the optional-parameter trick

`ContentService`, `LayoutService`, and `MemberService` are constructed directly in 51 existing
unit tests (`new ContentService(db, fakeUser)`, `new MemberService(db)`, etc.). To add caching
without touching any of those 51 call sites (and without risking a test silently relying on stale
cached data across assertions), the new dependency is an **optional constructor parameter
defaulting to `null`**:

```csharp
public class MemberService(IApplicationDbContext db, ICacheService? cache = null) : IMemberService
{
    private ICacheService Cache => cache ?? NoOpCacheService.Instance;
    ...
}
```

- **Production**: ASP.NET Core's DI container resolves `ICacheService` from the registered
  `MemoryCacheService` regardless of the default value - the default is only used when nothing
  supplies the parameter.
- **Existing tests**: `new MemberService(db)` keeps compiling and behaving exactly as before -
  `NoOpCacheService` always calls straight through to the factory and never actually caches.

This is the pattern to follow for any future cached service: add `ICacheService? cache = null` to
the primary constructor, expose it via a `private ICacheService Cache => cache ?? NoOpCacheService.Instance;`
property, and existing tests need no changes.

## Configuration

New `Cache` section in `appsettings.json` (also overridable via `Cache__*` env vars in
`docker-compose.yml`/`.env`, matching the existing `Smtp__*`/`Jwt__*` convention):

| Key | Default | Effect |
|---|---|---|
| `Cache:Enabled` | `true` | Global kill switch - `false` disables all caching everywhere |
| `Cache:ContentDurationSeconds` | `300` | How long CMS content (`content:all`, `content:{id}`) stays cached |
| `Cache:LayoutDurationSeconds` | `300` | How long the CMS layout list (`layout:all`) stays cached |
| `Cache:GracePeriodDurationSeconds` | `600` | How long the membership grace-period config value stays cached |

## Adding a new cached read in the future

1. Wrap the existing query: `return Cache.GetOrCreateAsync(key, "Cache:YourNewDurationSeconds", defaultSeconds, () => <existing query>);`
2. Add one `Cache.Remove(key)` call per mutation method that could make that key stale.
3. Add the new `Cache:YourNewDurationSeconds` key to `appsettings.json` (and optionally
   `docker-compose.yml`/`.env.example` if it should be deployment-tunable).
4. If the query result differs per caller (e.g. per-user data), **do not** use a single shared
   cache key - either don't cache it, or key it per-caller (e.g. `$"my-data:{userId}"`) and make
   sure every mutation path that changes that caller's data invalidates that specific key.

## Performance impact

- `GET /api/content` and `GET /api/content/{id}`: skips a full-table/single-row Postgres
  round-trip on every repeat read within the cache window - the biggest win, since every
  authenticated user hits this on effectively every page load.
- `GET /api/layouts`: same win, smaller table.
- `GET /api/members`, `GET /api/members/{id}`, `GET /api/members/me`: removes one extra
  `SystemConfigs` lookup per call - a small per-call win, but on the highest-traffic endpoint
  group in the app, so it adds up under load.
- No change to write-path latency (`Create`/`Update`/`Delete`/`Approve`/`Reject`) beyond one
  cheap `IMemoryCache.Remove` call per mutation.
- No behavior change for callers - same endpoints, same response shapes, same authorization
  rules; the only observable difference is that a repeat read within the cache window no longer
  reflects a *manual, out-of-band* database edit until the entry expires (irrelevant for Content/
  Layout, which are only ever changed through the app's own Create/Update/Delete endpoints, which
  invalidate correctly).
