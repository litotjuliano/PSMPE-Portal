# Audit Log, Error Log & System Logs Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a generic `AuditLog` table (rate-limit rejections, account lockouts, email-throttle
blocks, membership approvals), a dedicated `ErrorLog` table (backend unhandled exceptions +
frontend runtime errors, with net-new frontend error capture), 90/30-day retention pruning, and a
Super-Admin-only "System Logs" page to view both — per
`openspec/changes/add-audit-and-error-logs/proposal.md` and its two spec deltas.

**Architecture:** Two new EF Core entities behind two thin Infrastructure services
(`IAuditLogService`, `IErrorLogService`) mirroring the existing `IEmailSendThrottle` pattern —
write methods that never throw (best-effort), and query methods for pagination. Four write
points wire into existing code (`RateLimitingServiceExtensions.OnRejected`, `AuthController`
×3, `MemberService.ApproveAsync`), plus one new endpoint (`POST /api/errors/frontend`) for
frontend capture. A single new `BackgroundService` — the first scheduled job in this codebase —
prunes both tables daily. One new `SystemLogsController` (Super-Admin-only) serves both list
views; one new frontend page (`SystemLogsPage`, two tabs) consumes them.

**Sequencing:** Tasks 1–11 ship the Audit Log slice end-to-end (writes, retention, query, UI) as
independently working software. Tasks 12–18 add the Error Log slice on top of the same page.
Task 19 is final verification and docs.

**Tech Stack:** .NET 8 + EF Core 8 (Npgsql in prod, EF InMemory in tests) for the backend; React
19 + Vite + TypeScript + Tailwind for the frontend. Backend: xUnit unit tests
(`PSMPE.Portal.Application.UnitTests`, `PSMPE.Portal.Infrastructure.UnitTests`) and xUnit
integration tests (`PSMPE.Portal.WebAPI.IntegrationTests`, real HTTP via
`WebApplicationFactory<Program>`). Frontend has no test runner — verification is `tsc`/`eslint`
plus a manual browser pass.

**Before starting:** read `openspec/changes/add-audit-and-error-logs/proposal.md` and both files
under `specs/`. **Stop the local dev API before building** — it locks the output DLLs and
`dotnet build` fails with MSB3027 otherwise.

---

## 1. Domain entities and DbContext wiring

**Files:**
- Create: `src/PSMPE.Portal.Domain/Entities/AuditLog.cs`
- Create: `src/PSMPE.Portal.Domain/Entities/ErrorLog.cs`
- Create: `src/PSMPE.Portal.Domain/Enums/ErrorSource.cs`
- Modify: `src/PSMPE.Portal.Application/Common/Interfaces/IApplicationDbContext.cs`
- Modify: `src/PSMPE.Portal.Infrastructure/Persistence/ApplicationDbContext.cs`
- Modify: `tests/PSMPE.Portal.Application.UnitTests/TestSupport/TestDbContext.cs`

Pure data classes and DI plumbing — no meaningful behavior to TDD here; verification is a
successful build.

- [ ] **Step 1: Create the `ErrorSource` enum**

```csharp
namespace PSMPE.Portal.Domain.Enums;

public enum ErrorSource
{
    Backend = 0,
    Frontend = 1,
}
```

- [ ] **Step 2: Create the `AuditLog` entity**

```csharp
namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// One row per audited event, from any domain - a single generic table rather than a dedicated
/// history table per event type (see PrcVerificationHistory for that older pattern, and
/// add-audit-and-error-logs/proposal.md for why this one is generic). Rows are never updated,
/// only inserted and, for auth.* event types, eventually pruned - see LogRetentionService.
/// </summary>
public class AuditLog : BaseEntity
{
    public string EventType { get; set; } = string.Empty;
    public Guid? ActorUserId { get; set; }
    public string? ActorIp { get; set; }
    public string? TargetType { get; set; }
    public Guid? TargetId { get; set; }
    public string? Metadata { get; set; }
}
```

- [ ] **Step 3: Create the `ErrorLog` entity**

```csharp
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// One row per unhandled exception, backend or frontend. Separate from AuditLog - see
/// add-audit-and-error-logs/proposal.md's "ErrorLog is a separate table" decision.
/// </summary>
public class ErrorLog : BaseEntity
{
    public ErrorSource Source { get; set; }
    public string? ExceptionType { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
    public string? RequestPath { get; set; }
    public string? RequestMethod { get; set; }
    public string? Url { get; set; }
    public Guid? UserId { get; set; }
    public string? UserAgent { get; set; }
    public string? Metadata { get; set; }
}
```

- [ ] **Step 4: Add both DbSets to `IApplicationDbContext`**

In `src/PSMPE.Portal.Application/Common/Interfaces/IApplicationDbContext.cs`, add after
`DbSet<Payment> Payments { get; }`:

```csharp
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<ErrorLog> ErrorLogs { get; }
```

- [ ] **Step 5: Add both DbSets to `ApplicationDbContext`**

In `src/PSMPE.Portal.Infrastructure/Persistence/ApplicationDbContext.cs`, add after
`public DbSet<Payment> Payments => Set<Payment>();`:

```csharp
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ErrorLog> ErrorLogs => Set<ErrorLog>();
```

- [ ] **Step 6: Add both DbSets to `TestDbContext`**

In `tests/PSMPE.Portal.Application.UnitTests/TestSupport/TestDbContext.cs`, add the same two
properties (same syntax as Step 5) so Application-layer unit tests can seed/assert against both
tables.

- [ ] **Step 7: Build to confirm everything compiles**

Run: `dotnet build src/PSMPE.Portal.sln`
Expected: build succeeds (0 errors). Both new entities are now part of the EF model but have no
table yet — that's Task 2.

- [ ] **Step 8: Commit**

```bash
git add src/PSMPE.Portal.Domain/Entities/AuditLog.cs src/PSMPE.Portal.Domain/Entities/ErrorLog.cs \
  src/PSMPE.Portal.Domain/Enums/ErrorSource.cs \
  src/PSMPE.Portal.Application/Common/Interfaces/IApplicationDbContext.cs \
  src/PSMPE.Portal.Infrastructure/Persistence/ApplicationDbContext.cs \
  tests/PSMPE.Portal.Application.UnitTests/TestSupport/TestDbContext.cs
git commit -m "feat: add AuditLog and ErrorLog entities"
```

---

## 2. EF configurations and migration

**Files:**
- Create: `src/PSMPE.Portal.Infrastructure/Persistence/Configurations/AuditLogConfiguration.cs`
- Create: `src/PSMPE.Portal.Infrastructure/Persistence/Configurations/ErrorLogConfiguration.cs`
- Create: a new migration under `src/PSMPE.Portal.Infrastructure/Persistence/Migrations`

- [ ] **Step 1: Create `AuditLogConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(a => a.EventType).IsRequired().HasMaxLength(64);
        builder.Property(a => a.ActorIp).HasMaxLength(64);
        builder.Property(a => a.TargetType).HasMaxLength(64);

        // The pruning job filters on EventType/CreatedAt; the Audit tab's Event Type filter and
        // date range filter drive the same two columns.
        builder.HasIndex(a => a.CreatedAt);
        builder.HasIndex(a => a.EventType);
    }
}
```

- [ ] **Step 2: Create `ErrorLogConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class ErrorLogConfiguration : IEntityTypeConfiguration<ErrorLog>
{
    public void Configure(EntityTypeBuilder<ErrorLog> builder)
    {
        builder.Property(e => e.ExceptionType).HasMaxLength(256);
        builder.Property(e => e.Message).IsRequired().HasMaxLength(2000);
        builder.Property(e => e.StackTrace).HasMaxLength(8000);
        builder.Property(e => e.RequestPath).HasMaxLength(512);
        builder.Property(e => e.RequestMethod).HasMaxLength(16);
        builder.Property(e => e.Url).HasMaxLength(512);
        builder.Property(e => e.UserAgent).HasMaxLength(512);

        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => e.Source);
    }
}
```

- [ ] **Step 3: Generate the migration**

Run:
```bash
dotnet ef migrations add AddAuditAndErrorLogs \
  --project src/PSMPE.Portal.Infrastructure/PSMPE.Portal.Infrastructure.csproj \
  --startup-project src/PSMPE.Portal.WebAPI/PSMPE.Portal.WebAPI.csproj \
  --output-dir Persistence/Migrations
```
Expected: a new `<timestamp>_AddAuditAndErrorLogs.cs` (+ `.Designer.cs`) under
`src/PSMPE.Portal.Infrastructure/Persistence/Migrations`, and an updated
`ApplicationDbContextModelSnapshot.cs`.

- [ ] **Step 4: Inspect the generated migration**

Open the new migration file and confirm it only creates two new tables (`AuditLogs`, `ErrorLogs`)
with the columns/indexes from Steps 1–2 above — no unintended changes to any existing table.

- [ ] **Step 5: Commit**

```bash
git add src/PSMPE.Portal.Infrastructure/Persistence/Configurations/AuditLogConfiguration.cs \
  src/PSMPE.Portal.Infrastructure/Persistence/Configurations/ErrorLogConfiguration.cs \
  src/PSMPE.Portal.Infrastructure/Persistence/Migrations
git commit -m "feat: add EF configuration and migration for AuditLog/ErrorLog"
```

---

## 3. `IAuditLogService` write path

**Files:**
- Create: `src/PSMPE.Portal.Application/Common/Models/AuditLogDto.cs`
- Create: `src/PSMPE.Portal.Application/Common/Interfaces/IAuditLogService.cs`
- Create: `src/PSMPE.Portal.Infrastructure/Services/AuditLogService.cs`
- Modify: `src/PSMPE.Portal.Infrastructure/DependencyInjection.cs`
- Test: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/AuditLogServiceTests.cs`

The query half (`GetPagedAsync`) is added later in Task 9, once there's UI to consume it —
Task 3 only needs the write path to unblock Tasks 4–7.

- [ ] **Step 1: Create `AuditLogDto`**

```csharp
namespace PSMPE.Portal.Application.Common.Models;

public record AuditLogDto(
    Guid Id, string EventType, Guid? ActorUserId, string? ActorIp,
    string? TargetType, Guid? TargetId, string? Metadata, DateTimeOffset CreatedAt);
```

- [ ] **Step 2: Create `IAuditLogService`**

```csharp
using PSMPE.Portal.Application.Common.Models;

namespace PSMPE.Portal.Application.Common.Interfaces;

public interface IAuditLogService
{
    /// <summary>Best-effort: never throws. A logging failure must not break the caller's
    /// request - see Task 3's test for the contract this guarantees.</summary>
    Task RecordAsync(
        string eventType, Guid? actorUserId, string? actorIp, string? targetType, Guid? targetId,
        string? metadata, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Write the failing test**

Create `tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/AuditLogServiceTests.cs`. Uses
the integration test host (not a dedicated unit-test project) so it exercises the real
`ApplicationDbContext` exactly like `RateLimitingTests.cs` does, per the same rationale: these are
thin CRUD wrappers, and real DB wiring is the thing worth testing.

```csharp
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Infrastructure.Persistence;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Infrastructure;

public class AuditLogServiceTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public AuditLogServiceTests(CustomWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RecordAsync_PersistsAllFields()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        await service.RecordAsync("membership.approved", actorId, "203.0.113.5", "Member", targetId, "{\"membershipNo\":\"000123\"}");

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.AuditLogs);
        Assert.Equal("membership.approved", row.EventType);
        Assert.Equal(actorId, row.ActorUserId);
        Assert.Equal("203.0.113.5", row.ActorIp);
        Assert.Equal("Member", row.TargetType);
        Assert.Equal(targetId, row.TargetId);
        Assert.Equal("{\"membershipNo\":\"000123\"}", row.Metadata);
    }

    [Fact]
    public async Task RecordAsync_NullActorAndTarget_PersistsAsNull()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        await service.RecordAsync("auth.rate_limit.rejected", null, "203.0.113.9", null, null, null);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.AuditLogs);
        Assert.Null(row.ActorUserId);
        Assert.Null(row.TargetType);
        Assert.Null(row.TargetId);
        Assert.Null(row.Metadata);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter AuditLogServiceTests`
Expected: FAIL to build — `IAuditLogService` has no registered implementation yet.

- [ ] **Step 5: Implement `AuditLogService`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Services;

public class AuditLogService(IApplicationDbContext db, ILogger<AuditLogService> logger) : IAuditLogService
{
    public async Task RecordAsync(
        string eventType, Guid? actorUserId, string? actorIp, string? targetType, Guid? targetId,
        string? metadata, CancellationToken cancellationToken = default)
    {
        try
        {
            db.AuditLogs.Add(new AuditLog
            {
                EventType = eventType,
                ActorUserId = actorUserId,
                ActorIp = actorIp,
                TargetType = targetType,
                TargetId = targetId,
                Metadata = metadata,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort by design - see IAuditLogService.RecordAsync's doc comment. A failure
            // here must never turn a 429, a login, or an approval into a 500.
            logger.LogError(ex, "Failed to record audit log event {EventType}", eventType);
        }
    }
}
```

- [ ] **Step 6: Register in DI**

In `src/PSMPE.Portal.Infrastructure/DependencyInjection.cs`, add after
`services.AddSingleton<IEmailSendThrottle, MemoryCacheEmailSendThrottle>();`:

```csharp
        services.AddScoped<IAuditLogService, AuditLogService>();
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter AuditLogServiceTests`
Expected: PASS (2 tests).

- [ ] **Step 8: Add a test proving best-effort semantics**

Append to `AuditLogServiceTests.cs` — proves a broken database doesn't propagate:

```csharp
    [Fact]
    public async Task RecordAsync_WhenSaveFails_DoesNotThrow()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureDeletedAsync(); // breaks subsequent SaveChangesAsync calls on this context
        var service = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        var exception = await Record.ExceptionAsync(() =>
            service.RecordAsync("auth.rate_limit.rejected", null, "203.0.113.9", null, null, null));

        Assert.Null(exception);
    }
```

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter AuditLogServiceTests`
Expected: PASS (3 tests).

- [ ] **Step 9: Commit**

```bash
git add src/PSMPE.Portal.Application/Common/Models/AuditLogDto.cs \
  src/PSMPE.Portal.Application/Common/Interfaces/IAuditLogService.cs \
  src/PSMPE.Portal.Infrastructure/Services/AuditLogService.cs \
  src/PSMPE.Portal.Infrastructure/DependencyInjection.cs \
  tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/AuditLogServiceTests.cs
git commit -m "feat: add AuditLogService write path"
```

---

## 4. Wire rate-limiter 429 rejections into AuditLog

**Files:**
- Modify: `src/PSMPE.Portal.WebAPI/Extensions/RateLimitingServiceExtensions.cs`
- Test: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/AuditLogWritingTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/AuditLogWritingTests.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Auth;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Infrastructure;

public class AuditLogWritingTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuditLogWritingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static string UniqueIp() => AuthTestHelpers.NextClientIp(secondOctet: 21);

    [Fact]
    public async Task ExceedingLoginRateLimit_WritesAuditLogRow()
    {
        var ip = UniqueIp();
        for (var i = 0; i < 21; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new LoginRequest($"{Guid.NewGuid()}@example.com", "Password123!"))
            };
            request.Headers.Add("X-Forwarded-For", ip);
            await _client.SendAsync(request);
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.AuditLogs, a => a.ActorIp == ip);
        Assert.Equal("auth.rate_limit.rejected", row.EventType);
        Assert.Null(row.ActorUserId);
        Assert.Contains("auth-ip", row.Metadata);
    }

    [Fact]
    public async Task GlobalCeilingRejectingAnAlreadyAuthenticatedCaller_StillAuditsWithNullActor()
    {
        // ActorUserId is always null for this event type, even for an already-logged-in caller -
        // app.UseRateLimiter() runs before app.UseAuthentication() in Program.cs, deliberately
        // (see its comment: the global ceiling has to protect the auth surface itself, not just
        // requests that already passed it), so no authenticated identity is ever available yet
        // at the point a rejection occurs. This test's bearer token proves the caller COULD have
        // authenticated - proving ActorUserId is still null despite that.
        using var setupScope = _factory.Services.CreateScope();
        var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var (_, token) = await _client.CreatePrivilegedUserAsync(userManager, RoleNames.Admin);

        var ip = UniqueIp();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 301; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/roles").WithBearer(token);
            request.Headers.Add("X-Forwarded-For", ip);
            last = await _client.SendAsync(request);
        }

        Assert.Equal(System.Net.HttpStatusCode.TooManyRequests, last!.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.AuditLogs, a => a.ActorIp == ip);
        Assert.Null(row.ActorUserId);
        Assert.Contains("global", row.Metadata);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter AuditLogWritingTests`
Expected: FAIL — no `AuditLog` rows exist yet, since nothing writes them on rejection.

- [ ] **Step 3: Wire the write into `OnRejected`**

In `src/PSMPE.Portal.WebAPI/Extensions/RateLimitingServiceExtensions.cs`, add imports:

```csharp
using System.Text.Json;
using PSMPE.Portal.Application.Common.Interfaces;
```

Then, inside `options.OnRejected = async (context, cancellationToken) => { ... }`, as the first
statement in the block (before the existing `Retry-After` handling):

```csharp
                var policyName = context.HttpContext.GetEndpoint()?.Metadata
                    .GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "global";
                var actorIp = ClientIpPartitionKey(context.HttpContext, knownNetworks.Value);

                // ActorUserId is always null here - UseRateLimiter() runs before
                // UseAuthentication() in Program.cs (deliberately, so the global ceiling
                // protects the auth surface itself), so no authenticated identity is ever
                // available yet at this point in the pipeline, regardless of caller or policy.
                await context.HttpContext.RequestServices.GetRequiredService<IAuditLogService>()
                    .RecordAsync(
                        "auth.rate_limit.rejected", actorUserId: null, actorIp, targetType: null, targetId: null,
                        JsonSerializer.Serialize(new { policy = policyName }), cancellationToken);
```

`IAuditLogService.RecordAsync` never throws (Task 3), so this needs no extra try/catch here.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter AuditLogWritingTests`
Expected: PASS (2 tests). Also re-run the existing rate-limiting suite to confirm no regression:
`dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter RateLimitingTests`

- [ ] **Step 5: Commit**

```bash
git add src/PSMPE.Portal.WebAPI/Extensions/RateLimitingServiceExtensions.cs \
  tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/AuditLogWritingTests.cs
git commit -m "feat: audit rate-limiter rejections"
```

---

## 5. Wire account lockout into AuditLog

**Files:**
- Modify: `src/PSMPE.Portal.WebAPI/Controllers/AuthController.cs`
- Test: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/AuditLogWritingTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `AuditLogWritingTests.cs`:

```csharp
    [Fact]
    public async Task TrippingTheLockoutThreshold_WritesExactlyOneAuditLogRow()
    {
        using var setupScope = _factory.Services.CreateScope();
        var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{Guid.NewGuid()}@example.com";
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = "Lockout Test", EmailConfirmed = true };
        await userManager.CreateAsync(user, "Password123!");

        // Default MaxFailedAccessAttempts is 5 - see DependencyInjection.cs's Lockout options.
        for (var i = 0; i < 5; i++)
        {
            var ip = UniqueIp();
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new LoginRequest(email, "WrongPassword!"))
            };
            request.Headers.Add("X-Forwarded-For", ip);
            await _client.SendAsync(request);
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.AuditLogs, a => a.EventType == "auth.account.locked_out");
        Assert.Equal(user.Id, row.ActorUserId);

        // A follow-up attempt against the now-locked account must not write a second row.
        var extra = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new LoginRequest(email, "WrongPassword!"))
        };
        extra.Headers.Add("X-Forwarded-For", UniqueIp());
        await _client.SendAsync(extra);
        Assert.Single(db.AuditLogs, a => a.EventType == "auth.account.locked_out");
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter TrippingTheLockoutThreshold`
Expected: FAIL — no `auth.account.locked_out` row exists.

- [ ] **Step 3: Inject `IAuditLogService` into `AuthController` and wire the write**

In `src/PSMPE.Portal.WebAPI/Controllers/AuthController.cs`, add `IAuditLogService auditLogService`
to the constructor parameter list (after `IEmailSendThrottle emailSendThrottle,`):

```csharp
    IAuditLogService auditLogService,
```

Then in `Login`, replace:

```csharp
        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);

            // Re-checked immediately so the attempt that trips the threshold says so, rather
            // than returning a plain 401 and only reporting the lockout on the next try.
            if (await userManager.IsLockedOutAsync(user))
            {
                return StatusCode(403, new { message = lockedMessage, code = "ACCOUNT_LOCKED" });
            }

            return Unauthorized(new { message = genericFailure });
        }
```

with:

```csharp
        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);

            // Re-checked immediately so the attempt that trips the threshold says so, rather
            // than returning a plain 401 and only reporting the lockout on the next try. This is
            // also the only place that writes auth.account.locked_out - the check at the top of
            // this method (an already-locked account) never reaches here.
            if (await userManager.IsLockedOutAsync(user))
            {
                await auditLogService.RecordAsync(
                    "auth.account.locked_out", user.Id, actorIp: null, targetType: null, targetId: null, metadata: null);
                return StatusCode(403, new { message = lockedMessage, code = "ACCOUNT_LOCKED" });
            }

            return Unauthorized(new { message = genericFailure });
        }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter TrippingTheLockoutThreshold`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PSMPE.Portal.WebAPI/Controllers/AuthController.cs \
  tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/AuditLogWritingTests.cs
git commit -m "feat: audit account lockouts"
```

---

## 6. Wire email-throttle blocks into AuditLog

**Files:**
- Modify: `src/PSMPE.Portal.WebAPI/Controllers/AuthController.cs`
- Test: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/AuditLogWritingTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `AuditLogWritingTests.cs`:

```csharp
    [Fact]
    public async Task ExceedingTheEmailSendThrottle_WritesAuditLogRow()
    {
        using var setupScope = _factory.Services.CreateScope();
        var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{Guid.NewGuid()}@example.com";
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = "Throttle Test", EmailConfirmed = true };
        await userManager.CreateAsync(user, "Password123!");

        // Default RateLimit:EmailSendPerAddress:PermitLimit is 3 - see MemoryCacheEmailSendThrottle.
        for (var i = 0; i < 4; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgot-password")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new ForgotPasswordRequest(email))
            };
            request.Headers.Add("X-Forwarded-For", UniqueIp());
            await _client.SendAsync(request);
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.AuditLogs, a => a.EventType == "auth.email_throttle.blocked");
        Assert.Contains(email, row.Metadata);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter ExceedingTheEmailSendThrottle`
Expected: FAIL — no `auth.email_throttle.blocked` row exists.

- [ ] **Step 3: Wire the writes in `ResendVerificationEmail` and `ForgotPassword`**

In `src/PSMPE.Portal.WebAPI/Controllers/AuthController.cs`, add `using System.Text.Json;` to the
imports, then in `ResendVerificationEmail`, replace:

```csharp
        if (!emailSendThrottle.TryRecordSend(request.Email))
        {
            return Ok(new ResendVerificationEmailResponse(genericMessage));
        }
```

with:

```csharp
        if (!emailSendThrottle.TryRecordSend(request.Email))
        {
            await auditLogService.RecordAsync(
                "auth.email_throttle.blocked", user.Id, actorIp: null, targetType: null, targetId: null,
                JsonSerializer.Serialize(new { email = request.Email }));
            return Ok(new ResendVerificationEmailResponse(genericMessage));
        }
```

And in `ForgotPassword`, replace:

```csharp
        if (!emailSendThrottle.TryRecordSend(request.Email))
        {
            // Same generic response as every other path here - a throttled caller must not be
            // able to tell they were throttled, let alone that the account exists.
            return Ok(new ForgotPasswordResponse(genericMessage));
        }
```

with:

```csharp
        if (!emailSendThrottle.TryRecordSend(request.Email))
        {
            // Same generic response as every other path here - a throttled caller must not be
            // able to tell they were throttled, let alone that the account exists.
            await auditLogService.RecordAsync(
                "auth.email_throttle.blocked", user.Id, actorIp: null, targetType: null, targetId: null,
                JsonSerializer.Serialize(new { email = request.Email }));
            return Ok(new ForgotPasswordResponse(genericMessage));
        }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter ExceedingTheEmailSendThrottle`
Expected: PASS. Also re-run `AuditLogWritingTests` and `RateLimitingTests` in full to confirm no
regression.

- [ ] **Step 5: Commit**

```bash
git add src/PSMPE.Portal.WebAPI/Controllers/AuthController.cs \
  tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/AuditLogWritingTests.cs
git commit -m "feat: audit email-throttle blocks"
```

---

## 7. Wire membership approval into AuditLog

**Files:**
- Modify: `src/PSMPE.Portal.Application/Members/MemberService.cs`
- Test: `tests/PSMPE.Portal.Application.UnitTests/Members/MemberServiceTests.cs`

Unlike Tasks 4–6, this write goes directly through `IApplicationDbContext.AuditLogs` — no
`IAuditLogService` call — because `MemberService.ApproveAsync` already owns `db` and already
calls `SaveChangesAsync`, so adding the row to that same call keeps it atomic with the approval
for free (see proposal.md's "writes are best-effort... the membership-approval event is the
exception" decision).

- [ ] **Step 1: Write the failing tests**

Add to `tests/PSMPE.Portal.Application.UnitTests/Members/MemberServiceTests.cs`, near the other
`ApproveAsync` tests:

`ApproveAsync` isn't tested anywhere in this file yet — there are no neighboring `ApproveAsync_*`
tests to mirror. Its real requirements (confirmed by reading `MemberService.ApproveAsync` and
`ApproveMemberRequest`/`RecordPaymentRequest` in `MemberDto.cs`): the member's `PrcIdVerified`
must already be `true`, and it needs a resolvable registration payment — since
`SeedSubmittedMemberAsync` doesn't create one, supply it inline via `RecordPaymentRequest` rather
than pre-seeding a `Payment` row.

```csharp
    [Fact]
    public async Task ApproveAsync_WritesAuditLogRow()
    {
        using var db = TestDbContext.CreateInMemory();
        var member = await SeedSubmittedMemberAsync(db);
        member.PrcIdVerified = true;
        await db.SaveChangesAsync();
        var service = new MemberService(db);
        var adminId = Guid.NewGuid();
        var payment = new RecordPaymentRequest(500m, "REF-001", DateOnly.FromDateTime(DateTime.UtcNow), "uploads/proof.jpg");

        await service.ApproveAsync(member.Id, new ApproveMemberRequest("000123", payment), adminId, CancellationToken.None);

        var row = Assert.Single(db.AuditLogs);
        Assert.Equal("membership.approved", row.EventType);
        Assert.Equal(adminId, row.ActorUserId);
        Assert.Equal("Member", row.TargetType);
        Assert.Equal(member.Id, row.TargetId);
        Assert.Contains("000123", row.Metadata);
    }

    [Fact]
    public async Task ApproveAsync_ReApprovingAlreadyApprovedMember_WritesNoAdditionalAuditLogRow()
    {
        using var db = TestDbContext.CreateInMemory();
        var member = await SeedSubmittedMemberAsync(db);
        member.PrcIdVerified = true;
        await db.SaveChangesAsync();
        var service = new MemberService(db);
        var adminId = Guid.NewGuid();
        var payment = new RecordPaymentRequest(500m, "REF-001", DateOnly.FromDateTime(DateTime.UtcNow), "uploads/proof.jpg");
        await service.ApproveAsync(member.Id, new ApproveMemberRequest("000123", payment), adminId, CancellationToken.None);

        // Second call: the member now has an existing (accepted) payment, so pass Payment: null -
        // ApproveAsync's short-circuit on an already-approved member returns before that matters.
        await service.ApproveAsync(member.Id, new ApproveMemberRequest("000123", null), adminId, CancellationToken.None);

        Assert.Single(db.AuditLogs);
    }

    [Fact]
    public async Task ApproveAsync_FailedValidation_WritesNoAuditLogRow()
    {
        using var db = TestDbContext.CreateInMemory();
        var member = await SeedSubmittedMemberAsync(db); // PrcIdVerified left false
        var service = new MemberService(db);
        var payment = new RecordPaymentRequest(500m, "REF-001", DateOnly.FromDateTime(DateTime.UtcNow), "uploads/proof.jpg");

        await service.ApproveAsync(member.Id, new ApproveMemberRequest("000123", payment), Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(db.AuditLogs);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter ApproveAsync_WritesAuditLogRow`
Expected: FAIL — `db.AuditLogs` is empty after approval.

- [ ] **Step 3: Wire the write in `ApproveAsync`**

In `src/PSMPE.Portal.Application/Members/MemberService.cs`, add `using System.Text.Json;` to the
imports, then replace:

```csharp
        member.MembershipNo = trimmed;
        member.ApprovedAt = DateTimeOffset.UtcNow;
        member.UpdatedAt = DateTimeOffset.UtcNow;

        // Applied after ApprovedAt is set - the NewMembership due-date arithmetic reads it.
        PaymentVerification.Apply(paymentResult.Value!, member, decidedByUserId);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
```

with:

```csharp
        member.MembershipNo = trimmed;
        member.ApprovedAt = DateTimeOffset.UtcNow;
        member.UpdatedAt = DateTimeOffset.UtcNow;

        // Applied after ApprovedAt is set - the NewMembership due-date arithmetic reads it.
        PaymentVerification.Apply(paymentResult.Value!, member, decidedByUserId);

        // Added to the same SaveChangesAsync call, not a separate IAuditLogService write, so the
        // audit row is atomically all-or-nothing with the approval itself.
        db.AuditLogs.Add(new AuditLog
        {
            EventType = "membership.approved",
            ActorUserId = decidedByUserId,
            TargetType = "Member",
            TargetId = member.Id,
            Metadata = JsonSerializer.Serialize(new { membershipNo = trimmed }),
        });

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
```

Confirm `PSMPE.Portal.Domain.Entities` is already imported in this file (it is, for `Member` and
other entities) so `AuditLog` resolves without a new `using`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.Application.UnitTests --filter MemberServiceTests`
Expected: PASS, including every pre-existing `ApproveAsync_*` test (no regression).

- [ ] **Step 5: Commit**

```bash
git add src/PSMPE.Portal.Application/Members/MemberService.cs \
  tests/PSMPE.Portal.Application.UnitTests/Members/MemberServiceTests.cs
git commit -m "feat: audit membership approval"
```

---

## 8. Log retention (pruning)

**Files:**
- Create: `src/PSMPE.Portal.Application/Common/Interfaces/ILogRetentionService.cs`
- Create: `src/PSMPE.Portal.Infrastructure/Services/LogRetentionService.cs`
- Create: `src/PSMPE.Portal.Infrastructure/Services/LogRetentionBackgroundService.cs`
- Modify: `src/PSMPE.Portal.Infrastructure/DependencyInjection.cs`
- Test: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/LogRetentionServiceTests.cs`

This task covers the `AuditLog` half only (`ErrorLog` pruning is added in Task 15, once
`ErrorLog` exists as a concept the retention service needs to know about — the table itself
already exists from Task 1, but there's nothing writing to it yet).

- [ ] **Step 1: Create `ILogRetentionService`**

```csharp
namespace PSMPE.Portal.Application.Common.Interfaces;

public interface ILogRetentionService
{
    Task PruneAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/LogRetentionServiceTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Infrastructure.Persistence;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Infrastructure;

public class LogRetentionServiceTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public LogRetentionServiceTests(CustomWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PruneAsync_DeletesOldSecurityEvents_KeepsRecentOnes_NeverPrunesApprovals()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;

        var oldRejection = new AuditLog { EventType = "auth.rate_limit.rejected", CreatedAt = now.AddDays(-91) };
        var recentRejection = new AuditLog { EventType = "auth.account.locked_out", CreatedAt = now.AddDays(-89) };
        var oldApproval = new AuditLog { EventType = "membership.approved", CreatedAt = now.AddDays(-500) };
        db.AuditLogs.AddRange(oldRejection, recentRejection, oldApproval);
        await db.SaveChangesAsync();

        var retentionService = scope.ServiceProvider.GetRequiredService<ILogRetentionService>();
        await retentionService.PruneAsync();

        var remaining = db.AuditLogs.Select(a => a.Id).ToList();
        Assert.DoesNotContain(oldRejection.Id, remaining);
        Assert.Contains(recentRejection.Id, remaining);
        Assert.Contains(oldApproval.Id, remaining);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter LogRetentionServiceTests`
Expected: FAIL — no `ILogRetentionService` implementation registered.

- [ ] **Step 4: Implement `LogRetentionService`**

```csharp
using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.Infrastructure.Services;

public class LogRetentionService(IApplicationDbContext db, IDateTimeProvider dateTimeProvider) : ILogRetentionService
{
    private const int AuditSecurityEventRetentionDays = 90;

    public async Task PruneAsync(CancellationToken cancellationToken = default)
    {
        var auditCutoff = dateTimeProvider.UtcNow.AddDays(-AuditSecurityEventRetentionDays);
        var staleAuditRows = await db.AuditLogs
            .Where(a => a.EventType.StartsWith("auth.") && a.CreatedAt < auditCutoff)
            .ToListAsync(cancellationToken);
        db.AuditLogs.RemoveRange(staleAuditRows);

        await db.SaveChangesAsync(cancellationToken);
    }
}
```

(Uses `ToListAsync` + `RemoveRange` rather than EF Core's `ExecuteDeleteAsync` bulk operation,
because the InMemory provider used in tests doesn't support `ExecuteDelete` — this way the same
code path runs against both the test InMemory database and real Postgres.)

- [ ] **Step 5: Create `LogRetentionBackgroundService`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.Infrastructure.Services;

/// <summary>
/// The first scheduled job in this codebase - a plain daily PeriodicTimer rather than a
/// scheduling library, since there's exactly one job at exactly one interval. Runs once
/// immediately on startup (the do/while below checks its condition after the body runs), then
/// once every 24h after that, so a restart-heavy deployment doesn't wait a full day for its
/// first prune. Each tick opens its own DI scope, since IApplicationDbContext is scoped and this
/// service itself is a singleton for the app's lifetime.
/// </summary>
public class LogRetentionBackgroundService(
    IServiceScopeFactory scopeFactory, ILogger<LogRetentionBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var retentionService = scope.ServiceProvider.GetRequiredService<ILogRetentionService>();
                await retentionService.PruneAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Log retention pruning failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
```

- [ ] **Step 6: Register both in DI**

In `src/PSMPE.Portal.Infrastructure/DependencyInjection.cs`, add after the
`IAuditLogService` registration from Task 3:

```csharp
        services.AddScoped<ILogRetentionService, LogRetentionService>();
        services.AddHostedService<LogRetentionBackgroundService>();
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter LogRetentionServiceTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/PSMPE.Portal.Application/Common/Interfaces/ILogRetentionService.cs \
  src/PSMPE.Portal.Infrastructure/Services/LogRetentionService.cs \
  src/PSMPE.Portal.Infrastructure/Services/LogRetentionBackgroundService.cs \
  src/PSMPE.Portal.Infrastructure/DependencyInjection.cs \
  tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/LogRetentionServiceTests.cs
git commit -m "feat: prune stale AuditLog security events daily"
```

---

## 9. `IAuditLogService` query path (search/filter/pagination)

**Files:**
- Modify: `src/PSMPE.Portal.Application/Common/Interfaces/IAuditLogService.cs`
- Modify: `src/PSMPE.Portal.Infrastructure/Services/AuditLogService.cs`
- Test: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/AuditLogServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Add `using PSMPE.Portal.Domain.Entities;` to `AuditLogServiceTests.cs`'s imports — Task 3's tests
never spelled out the `AuditLog` type name directly (only via `var` inference through
`db.AuditLogs`), so this import wasn't needed until now, where `SeedThreeRowsAsync` below
constructs `AuditLog` instances explicitly. Then append to the file:

```csharp
    private static async Task SeedThreeRowsAsync(ApplicationDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.AuditLogs.AddRange(
            new AuditLog { EventType = "auth.rate_limit.rejected", ActorIp = "203.0.113.1", CreatedAt = now.AddDays(-1) },
            new AuditLog { EventType = "auth.account.locked_out", ActorIp = "203.0.113.2", CreatedAt = now.AddDays(-2) },
            new AuditLog
            {
                EventType = "membership.approved", TargetType = "Member", TargetId = Guid.NewGuid(),
                Metadata = "{\"membershipNo\":\"000999\"}", CreatedAt = now.AddDays(-3),
            });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetPagedAsync_OrdersNewestFirst()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SeedThreeRowsAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        var result = await service.GetPagedAsync(page: 1, pageSize: 20, search: null, eventType: null, from: null, to: null);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal("auth.rate_limit.rejected", result.Items[0].EventType);
        Assert.Equal("membership.approved", result.Items[2].EventType);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByEventType()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SeedThreeRowsAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        var result = await service.GetPagedAsync(page: 1, pageSize: 20, search: null, eventType: "membership.approved", from: null, to: null);

        var item = Assert.Single(result.Items);
        Assert.Equal("membership.approved", item.EventType);
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesMetadata()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SeedThreeRowsAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        var result = await service.GetPagedAsync(page: 1, pageSize: 20, search: "000999", eventType: null, from: null, to: null);

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByDateRange()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SeedThreeRowsAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        var result = await service.GetPagedAsync(
            page: 1, pageSize: 20, search: null, eventType: null,
            from: DateTimeOffset.UtcNow.AddDays(-2).AddHours(-1), to: DateTimeOffset.UtcNow.AddDays(-1).AddHours(1));

        Assert.Equal(2, result.TotalCount); // excludes the -3 day membership.approved row
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter AuditLogServiceTests`
Expected: FAIL to build — `GetPagedAsync` doesn't exist on `IAuditLogService` yet.

- [ ] **Step 3: Add `GetPagedAsync` to the interface**

In `IAuditLogService.cs`, add `using PSMPE.Portal.Application.Common.Models;` (already present
from Task 3's `AuditLogDto`) and append:

```csharp
    Task<PagedResult<AuditLogDto>> GetPagedAsync(
        int page, int pageSize, string? search, string? eventType, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement it in `AuditLogService`**

Add `using PSMPE.Portal.Application.Common.Models;` if not already present, then append to the
class:

```csharp
    public async Task<PagedResult<AuditLogDto>> GetPagedAsync(
        int page, int pageSize, string? search, string? eventType, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<AuditLog> query = db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim().ToLower();
            query = query.Where(a =>
                a.EventType.ToLower().Contains(normalized)
                || (a.TargetType != null && a.TargetType.ToLower().Contains(normalized))
                || (a.Metadata != null && a.Metadata.ToLower().Contains(normalized)));
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(a => a.EventType == eventType);
        }

        if (from is not null)
        {
            query = query.Where(a => a.CreatedAt >= from);
        }

        if (to is not null)
        {
            query = query.Where(a => a.CreatedAt <= to);
        }

        query = query.OrderByDescending(a => a.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new AuditLogDto(a.Id, a.EventType, a.ActorUserId, a.ActorIp, a.TargetType, a.TargetId, a.Metadata, a.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogDto>(items, totalCount, page, pageSize);
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter AuditLogServiceTests`
Expected: PASS (all tests in this file, including Task 3's).

- [ ] **Step 6: Commit**

```bash
git add src/PSMPE.Portal.Application/Common/Interfaces/IAuditLogService.cs \
  src/PSMPE.Portal.Infrastructure/Services/AuditLogService.cs \
  tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/AuditLogServiceTests.cs
git commit -m "feat: add AuditLog query path (search, filter, date range, pagination)"
```

---

## 10. `SystemLogsController` — GET /api/admin/audit-log

**Files:**
- Create: `src/PSMPE.Portal.WebAPI/Controllers/SystemLogsController.cs`
- Test: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Admin/SystemLogsControllerTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/PSMPE.Portal.WebAPI.IntegrationTests/Admin/SystemLogsControllerTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Admin;

public class SystemLogsControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SystemLogsControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetAuditLog_WithoutSuperAdmin_Returns403()
    {
        using var setupScope = _factory.Services.CreateScope();
        var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var (_, token) = await _client.CreatePrivilegedUserAsync(userManager, RoleNames.Admin);

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/admin/audit-log").WithBearer(token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAuditLog_AsSuperAdmin_ResolvesActorEmail()
    {
        using var setupScope = _factory.Services.CreateScope();
        var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var (adminId, adminEmail) = (Guid.Empty, string.Empty);
        var (_, token) = await _client.CreatePrivilegedUserAsync(userManager, RoleNames.SuperAdmin);

        var actor = new ApplicationUser { UserName = "actor@example.com", Email = "actor@example.com", DisplayName = "Actor", EmailConfirmed = true };
        await userManager.CreateAsync(actor, "Password123!");

        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AuditLogs.Add(new AuditLog
            {
                EventType = "membership.approved", ActorUserId = actor.Id, TargetType = "Member", TargetId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/admin/audit-log").WithBearer(token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var item = body.GetProperty("items")[0];
        Assert.Equal("actor@example.com", item.GetProperty("actorEmail").GetString());
    }

    [Fact]
    public async Task GetErrorLog_WithoutSuperAdmin_Returns403()
    {
        using var setupScope = _factory.Services.CreateScope();
        var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var (_, token) = await _client.CreatePrivilegedUserAsync(userManager, RoleNames.Admin);

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/admin/error-log").WithBearer(token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```

(`GetErrorLog_*` is included now so the controller can be built in one pass in Step 3 below, but
only the `GetAuditLog_*` tests are expected to pass until Task 16 adds `GetErrorLog`'s
implementation — the third test here only asserts the 403 gate, which `[Authorize]` on the
controller already provides regardless of which action exists.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter SystemLogsControllerTests`
Expected: FAIL — the route doesn't exist yet (404s instead of the expected status codes).

- [ ] **Step 3: Create `SystemLogsController`**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Infrastructure.Authorization.Policies;

namespace PSMPE.Portal.WebAPI.Controllers;

/// <summary>
/// Read-only views over AuditLog/ErrorLog, restricted to Super Admin - stricter than the general
/// RequireAdminOrApproval gate on the rest of /api/admin, matching how role assignment and user
/// deletion are already Super-Admin-only in AdminController.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = PolicyNames.RequireSuperAdmin)]
public class SystemLogsController(
    IAuditLogService auditLogService,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    public record AuditLogEntryDto(
        Guid Id, string EventType, Guid? ActorUserId, string? ActorEmail, string? ActorIp,
        string? TargetType, Guid? TargetId, string? Metadata, DateTimeOffset CreatedAt);

    [HttpGet("audit-log")]
    public async Task<ActionResult<PagedResult<AuditLogEntryDto>>> GetAuditLog(
        int page = 1, int pageSize = 20, string? search = null, string? eventType = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        var result = await auditLogService.GetPagedAsync(page, pageSize, search, eventType, from, to, cancellationToken);
        var actorEmails = await ResolveEmailsAsync(result.Items.Select(a => a.ActorUserId));

        var entries = result.Items
            .Select(a => new AuditLogEntryDto(
                a.Id, a.EventType, a.ActorUserId,
                a.ActorUserId is { } actorId ? actorEmails.GetValueOrDefault(actorId) : null,
                a.ActorIp, a.TargetType, a.TargetId, a.Metadata, a.CreatedAt))
            .ToList();

        return Ok(new PagedResult<AuditLogEntryDto>(entries, result.TotalCount, result.Page, result.PageSize));
    }

    /// <summary>Single query for the whole page, same shape as AdminController.GetUsers's role
    /// resolution - not a per-row lookup.</summary>
    private async Task<Dictionary<Guid, string>> ResolveEmailsAsync(IEnumerable<Guid?> userIds)
    {
        var ids = userIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await userManager.Users
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? string.Empty);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter SystemLogsControllerTests`
Expected: `GetAuditLog_WithoutSuperAdmin_Returns403` and `GetAuditLog_AsSuperAdmin_ResolvesActorEmail`
PASS. `GetErrorLog_WithoutSuperAdmin_Returns403` PASSes too, since `[Authorize]` on the
controller rejects unauthorized callers before routing needs a matching action — confirm this
directly rather than assuming it.

- [ ] **Step 5: Commit**

```bash
git add src/PSMPE.Portal.WebAPI/Controllers/SystemLogsController.cs \
  tests/PSMPE.Portal.WebAPI.IntegrationTests/Admin/SystemLogsControllerTests.cs
git commit -m "feat: add GET /api/admin/audit-log"
```

---

## 11. Frontend — System Logs page shell + Audit tab

**Files:**
- Create: `apps/web/src/core/api/endpoints/systemLogsApi.ts`
- Create: `apps/web/src/core/pages/SystemLogsPage.tsx`
- Create: `apps/web/src/integrations/template/pages/AuditLogTable.tsx`
- Create: `apps/web/src/integrations/template/components/shared/LogDetailsModal.tsx`
- Modify: `apps/web/src/integrations/template/index.ts`
- Modify: `apps/web/src/integrations/template/components/layout/SideNav/menu.ts`
- Modify: `apps/web/src/core/routes/router.tsx`

No test runner exists in `apps/web` (per repo convention) — verification here is `tsc`/`eslint`
plus a manual browser pass, listed at the end of this task.

- [ ] **Step 1: Create `systemLogsApi.ts`**

```typescript
import { apiClient } from '../apiClient'
import type { PagedResult } from './adminApi'

export interface AuditLogEntry {
  id: string
  eventType: string
  actorUserId: string | null
  actorEmail: string | null
  actorIp: string | null
  targetType: string | null
  targetId: string | null
  metadata: string | null
  createdAt: string
}

export interface GetAuditLogParams {
  page?: number
  pageSize?: number
  search?: string
  eventType?: string
  from?: string
  to?: string
}

export const systemLogsApi = {
  getAuditLog: (params: GetAuditLogParams = {}) =>
    apiClient.get<PagedResult<AuditLogEntry>>('/api/admin/audit-log', { params }).then((res) => res.data),
}
```

- [ ] **Step 2: Create `LogDetailsModal`**

Follows `ConfirmationModal`'s shell (backdrop, Escape-to-close, `card`/`card-header`/`card-body`)
but shows read-only JSON/text instead of a confirmation prompt:

```tsx
import { useEffect } from 'react'
import { StandardButton } from './StandardButton'

interface LogDetailsModalProps {
  isOpen: boolean
  title: string
  content: string | null
  onClose: () => void
}

export const LogDetailsModal = ({ isOpen, title, content, onClose }: LogDetailsModalProps) => {
  useEffect(() => {
    if (!isOpen) return
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [isOpen, onClose])

  if (!isOpen) return null

  return (
    <div className="fixed inset-0 z-100 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/50" onClick={onClose} />
      <div className="relative card w-full max-w-2xl">
        <div className="card-header">
          <h6 className="card-title">{title}</h6>
        </div>
        <div className="card-body">
          <pre className="text-xs whitespace-pre-wrap break-words max-h-96 overflow-y-auto bg-default-50 p-3 rounded">
            {content ?? '(no details)'}
          </pre>
        </div>
        <div className="card-footer flex items-center justify-end">
          <StandardButton variant="secondary" onClick={onClose}>
            Close
          </StandardButton>
        </div>
      </div>
    </div>
  )
}
```

- [ ] **Step 3: Create `AuditLogTable`**

```tsx
import { useState } from 'react'
import type { AuditLogEntry } from '../../../core/api/endpoints/systemLogsApi'
import { LogDetailsModal } from '../components/shared/LogDetailsModal'

const EVENT_TYPES = ['auth.rate_limit.rejected', 'auth.account.locked_out', 'auth.email_throttle.blocked', 'membership.approved']

interface AuditLogTableProps {
  entries: AuditLogEntry[]
  searchInput: string
  onSearchInputChange: (value: string) => void
  eventTypeFilter: string
  onEventTypeFilterChange: (value: string) => void
  from: string
  to: string
  onFromChange: (value: string) => void
  onToChange: (value: string) => void
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
}

export const AuditLogTable = ({
  entries, searchInput, onSearchInputChange, eventTypeFilter, onEventTypeFilterChange,
  from, to, onFromChange, onToChange, page, pageSize, totalCount, onPageChange,
}: AuditLogTableProps) => {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  const [detailsEntry, setDetailsEntry] = useState<AuditLogEntry | null>(null)

  return (
    <div className="card">
      <div className="flex flex-wrap items-center gap-3 px-5 py-3 border-b border-default-200 bg-default-50">
        <input
          type="text"
          className="form-input max-w-xs"
          placeholder="Search event type, target, metadata…"
          value={searchInput}
          onChange={(e) => onSearchInputChange(e.target.value)}
        />
        <select className="form-input max-w-xs" value={eventTypeFilter} onChange={(e) => onEventTypeFilterChange(e.target.value)}>
          <option value="">All event types</option>
          {EVENT_TYPES.map((type) => (
            <option key={type} value={type}>{type}</option>
          ))}
        </select>
        <input type="date" className="form-input" value={from} onChange={(e) => onFromChange(e.target.value)} />
        <span className="text-sm text-default-500">to</span>
        <input type="date" className="form-input" value={to} onChange={(e) => onToChange(e.target.value)} />
      </div>

      <div className="flex flex-col">
        <div className="overflow-x-auto">
          <div className="min-w-full inline-block align-middle">
            <div className="overflow-hidden">
              <table className="min-w-full divide-y divide-default-200">
                <thead className="bg-default-150">
                  <tr className="text-sm font-normal text-default-700 whitespace-nowrap">
                    <th className="px-3.5 py-3 text-start">Timestamp</th>
                    <th className="px-3.5 py-3 text-start">Event Type</th>
                    <th className="px-3.5 py-3 text-start">Actor</th>
                    <th className="px-3.5 py-3 text-start">IP</th>
                    <th className="px-3.5 py-3 text-start">Target</th>
                    <th className="px-3.5 py-3 text-start">Details</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-default-200">
                  {entries.map((entry) => (
                    <tr key={entry.id} className="text-default-800 font-normal text-sm whitespace-nowrap">
                      <td className="py-3 px-3.5">{new Date(entry.createdAt).toLocaleString()}</td>
                      <td className="py-3 px-3.5">{entry.eventType}</td>
                      <td className="py-3 px-3.5">{entry.actorEmail ?? '—'}</td>
                      <td className="py-3 px-3.5">{entry.actorIp ?? '—'}</td>
                      <td className="py-3 px-3.5">{entry.targetType ? `${entry.targetType}: ${entry.targetId}` : '—'}</td>
                      <td className="py-3 px-3.5">
                        <button type="button" className="text-primary hover:underline" onClick={() => setDetailsEntry(entry)}>
                          View
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>

      <div className="card-footer flex items-center justify-between">
        <span className="text-sm text-default-500">
          Page {page} of {totalPages} ({totalCount} total)
        </span>
        <div className="flex items-center gap-1.5">
          <button type="button" className="btn btn-sm border border-default-200 disabled:opacity-50" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
            Previous
          </button>
          <button type="button" className="btn btn-sm border border-default-200 disabled:opacity-50" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}>
            Next
          </button>
        </div>
      </div>

      <LogDetailsModal
        isOpen={detailsEntry !== null}
        title="Audit event details"
        content={detailsEntry?.metadata ?? null}
        onClose={() => setDetailsEntry(null)}
      />
    </div>
  )
}
```

- [ ] **Step 4: Create `SystemLogsPage`**

Audit-only for now — the Errors tab is added in Task 17, following the same `?tab=` URL pattern
`MembersPage` uses for `?queue=`.

```tsx
import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { systemLogsApi, type AuditLogEntry } from '../api/endpoints/systemLogsApi'
import { PageBreadcrumb, PageMeta, AuditLogTable } from '../../integrations/template'

const PAGE_SIZE = 20

export function SystemLogsPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const activeTab = searchParams.get('tab') === 'errors' ? 'errors' : 'audit'

  const [auditEntries, setAuditEntries] = useState<AuditLogEntry[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [page, setPage] = useState(1)
  const [loading, setLoading] = useState(true)

  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [eventTypeFilter, setEventTypeFilter] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')

  useEffect(() => {
    const timer = setTimeout(() => {
      setSearch(searchInput)
      setPage(1)
    }, 350)
    return () => clearTimeout(timer)
  }, [searchInput])

  useEffect(() => {
    if (activeTab !== 'audit') return
    let cancelled = false
    setLoading(true)
    systemLogsApi
      .getAuditLog({
        page, pageSize: PAGE_SIZE,
        ...(search ? { search } : {}),
        ...(eventTypeFilter ? { eventType: eventTypeFilter } : {}),
        ...(from ? { from } : {}),
        ...(to ? { to } : {}),
      })
      .then((result) => {
        if (cancelled) return
        setAuditEntries(result.items)
        setTotalCount(result.totalCount)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [activeTab, page, search, eventTypeFilter, from, to])

  const handleTabChange = (tab: 'audit' | 'errors') => {
    setSearchParams(tab === 'audit' ? {} : { tab })
    setPage(1)
  }

  return (
    <>
      <PageMeta title="System Logs" />
      <main>
        <PageBreadcrumb title="System Logs" />
        <div className="flex gap-2 mb-4">
          <button
            type="button"
            className={`btn btn-sm ${activeTab === 'audit' ? 'bg-primary text-white' : 'border border-default-300'}`}
            onClick={() => handleTabChange('audit')}
          >
            Audit
          </button>
          <button
            type="button"
            className={`btn btn-sm ${activeTab === 'errors' ? 'bg-primary text-white' : 'border border-default-300'}`}
            onClick={() => handleTabChange('errors')}
          >
            Errors
          </button>
        </div>
        {loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          activeTab === 'audit' && (
            <AuditLogTable
              entries={auditEntries}
              searchInput={searchInput}
              onSearchInputChange={setSearchInput}
              eventTypeFilter={eventTypeFilter}
              onEventTypeFilterChange={(value) => {
                setEventTypeFilter(value)
                setPage(1)
              }}
              from={from}
              to={to}
              onFromChange={(value) => {
                setFrom(value)
                setPage(1)
              }}
              onToChange={(value) => {
                setTo(value)
                setPage(1)
              }}
              page={page}
              pageSize={PAGE_SIZE}
              totalCount={totalCount}
              onPageChange={setPage}
            />
          )
        )}
      </main>
    </>
  )
}
```

- [ ] **Step 5: Export the new components from the template barrel**

In `apps/web/src/integrations/template/index.ts`, add:

```typescript
export { AuditLogTable } from './pages/AuditLogTable'
export { LogDetailsModal } from './components/shared/LogDetailsModal'
```

- [ ] **Step 6: Add the nav item**

In `apps/web/src/integrations/template/components/layout/SideNav/menu.ts`, add the icon to the
existing `react-icons/lu` import (`LuFileClock` alongside the others), then add a new entry after
`Roles`:

```typescript
  {
    key: 'SystemLogs',
    label: 'System Logs',
    icon: LuFileClock,
    href: '/admin/system-logs',
    requiredRoles: ['Super Admin'],
  },
```

- [ ] **Step 7: Add the route**

In `apps/web/src/core/routes/router.tsx`, import `SystemLogsPage` and add a Super-Admin-only
`ProtectedRoute` wrapper (a stricter sibling to the existing
`[Roles.Admin, Roles.SuperAdmin, Roles.Approval]` block), inside the existing `AppShell` children,
after the `/admin/roles` route's enclosing block:

```tsx
              {
                element: <ProtectedRoute requiredRoles={[Roles.SuperAdmin]} />,
                children: [
                  { path: '/admin/system-logs', element: <SystemLogsPage /> },
                ],
              },
```

- [ ] **Step 8: Verify**

Run: `npx tsc -b` (from `apps/web`) — expected: no errors.
Run: `npx eslint .` (from `apps/web`) — expected: no errors.

- [ ] **Step 9: Manual browser verification**

Not yet automatable — needs a running app and a browser:
- Log in as Super Admin, confirm "System Logs" appears in the nav and a non-Super-Admin doesn't
  see it (or gets redirected on direct URL entry).
- Trigger a real 429 (e.g. spam `/api/auth/username-available` past its limit) and an approval,
  then confirm both rows appear on the Audit tab with working search/event-type filter/date range,
  correct pagination, and a working "View" details modal.

- [ ] **Step 10: Commit**

```bash
git add apps/web/src/core/api/endpoints/systemLogsApi.ts apps/web/src/core/pages/SystemLogsPage.tsx \
  apps/web/src/integrations/template/pages/AuditLogTable.tsx \
  apps/web/src/integrations/template/components/shared/LogDetailsModal.tsx \
  apps/web/src/integrations/template/index.ts \
  apps/web/src/integrations/template/components/layout/SideNav/menu.ts \
  apps/web/src/core/routes/router.tsx
git commit -m "feat: add System Logs page with Audit tab"
```

**Audit Log slice complete: writes, retention, query, and UI all work end-to-end.**

---

## 12. `IErrorLogService` write path

**Files:**
- Create: `src/PSMPE.Portal.Application/Common/Models/ErrorLogDto.cs`
- Create: `src/PSMPE.Portal.Application/Common/Interfaces/IErrorLogService.cs`
- Create: `src/PSMPE.Portal.Infrastructure/Services/ErrorLogService.cs`
- Modify: `src/PSMPE.Portal.Infrastructure/DependencyInjection.cs`
- Test: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/ErrorLogServiceTests.cs`

- [ ] **Step 1: Create `ErrorLogDto`**

```csharp
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Common.Models;

public record ErrorLogDto(
    Guid Id, ErrorSource Source, string? ExceptionType, string Message, string? StackTrace,
    string? RequestPath, string? RequestMethod, string? Url, Guid? UserId, string? UserAgent,
    string? Metadata, DateTimeOffset CreatedAt);
```

- [ ] **Step 2: Create `IErrorLogService`**

```csharp
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Common.Interfaces;

public interface IErrorLogService
{
    /// <summary>Best-effort: never throws, same contract as IAuditLogService.RecordAsync.
    /// Message/StackTrace are truncated to the configured maximum before being persisted, so an
    /// oversized value from an untrusted frontend report can't fail the write outright.</summary>
    Task RecordAsync(
        ErrorSource source, string? exceptionType, string message, string? stackTrace,
        string? requestPath, string? requestMethod, string? url, Guid? userId, string? userAgent,
        string? metadata, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Write the failing tests**

Create `tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/ErrorLogServiceTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Infrastructure;

public class ErrorLogServiceTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public ErrorLogServiceTests(CustomWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RecordAsync_PersistsAllFields()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IErrorLogService>();
        var userId = Guid.NewGuid();

        await service.RecordAsync(
            ErrorSource.Backend, "System.InvalidOperationException", "boom", "at Foo.Bar()",
            "/api/members", "POST", url: null, userId, "TestAgent/1.0", metadata: null);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.ErrorLogs);
        Assert.Equal(ErrorSource.Backend, row.Source);
        Assert.Equal("boom", row.Message);
        Assert.Equal(userId, row.UserId);
    }

    [Fact]
    public async Task RecordAsync_TruncatesOversizedMessageAndStackTrace()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IErrorLogService>();

        await service.RecordAsync(
            ErrorSource.Frontend, exceptionType: null, new string('m', 3000), new string('s', 9000),
            requestPath: null, requestMethod: null, url: "https://example.com/", userId: null,
            userAgent: null, metadata: null);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.ErrorLogs);
        Assert.Equal(2000, row.Message!.Length);
        Assert.Equal(8000, row.StackTrace!.Length);
    }

    [Fact]
    public async Task RecordAsync_WhenSaveFails_DoesNotThrow()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureDeletedAsync();
        var service = scope.ServiceProvider.GetRequiredService<IErrorLogService>();

        var exception = await Record.ExceptionAsync(() =>
            service.RecordAsync(ErrorSource.Backend, null, "boom", null, null, null, null, null, null, null));

        Assert.Null(exception);
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter ErrorLogServiceTests`
Expected: FAIL to build — no implementation registered yet.

- [ ] **Step 5: Implement `ErrorLogService`**

```csharp
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Infrastructure.Services;

public class ErrorLogService(IApplicationDbContext db, ILogger<ErrorLogService> logger) : IErrorLogService
{
    private const int MaxMessageLength = 2000;
    private const int MaxStackTraceLength = 8000;

    public async Task RecordAsync(
        ErrorSource source, string? exceptionType, string message, string? stackTrace,
        string? requestPath, string? requestMethod, string? url, Guid? userId, string? userAgent,
        string? metadata, CancellationToken cancellationToken = default)
    {
        try
        {
            db.ErrorLogs.Add(new ErrorLog
            {
                Source = source,
                ExceptionType = exceptionType,
                Message = Truncate(message, MaxMessageLength) ?? string.Empty,
                StackTrace = Truncate(stackTrace, MaxStackTraceLength),
                RequestPath = requestPath,
                RequestMethod = requestMethod,
                Url = url,
                UserId = userId,
                UserAgent = userAgent,
                Metadata = metadata,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record error log entry for {Source}", source);
        }
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null : value.Length <= maxLength ? value : value[..maxLength];
}
```

- [ ] **Step 6: Register in DI**

In `DependencyInjection.cs`, add after the `IAuditLogService` registration:

```csharp
        services.AddScoped<IErrorLogService, ErrorLogService>();
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter ErrorLogServiceTests`
Expected: PASS (3 tests).

- [ ] **Step 8: Commit**

```bash
git add src/PSMPE.Portal.Application/Common/Models/ErrorLogDto.cs \
  src/PSMPE.Portal.Application/Common/Interfaces/IErrorLogService.cs \
  src/PSMPE.Portal.Infrastructure/Services/ErrorLogService.cs \
  src/PSMPE.Portal.Infrastructure/DependencyInjection.cs \
  tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/ErrorLogServiceTests.cs
git commit -m "feat: add ErrorLogService write path"
```

---

## 13. Wire backend unhandled exceptions into ErrorLog

**Files:**
- Modify: `src/PSMPE.Portal.WebAPI/Middleware/ExceptionHandlingMiddleware.cs`
- Test: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/ErrorLogWritingTests.cs`

- [ ] **Step 1: Write the failing test**

This needs an endpoint that reliably throws. Check whether the integration test suite already has
a test-only "throw" endpoint (search for one, e.g. `grep -rn "ThrowsException\|/api/test" tests/`
and `src/PSMPE.Portal.WebAPI/Controllers`); if not, add a minimal one gated to the `Testing`
environment only, e.g. in a new `src/PSMPE.Portal.WebAPI/Controllers/DiagnosticsController.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace PSMPE.Portal.WebAPI.Controllers;

/// <summary>Testing-only - lets integration tests exercise ExceptionHandlingMiddleware's real
/// unhandled-exception path without depending on some other endpoint happening to be breakable.
/// 404s outside the Testing environment.</summary>
[ApiController]
[Route("api/diagnostics")]
public class DiagnosticsController(IWebHostEnvironment env) : ControllerBase
{
    [HttpGet("throw")]
    public IActionResult Throw()
    {
        if (!env.IsEnvironment("Testing"))
        {
            return NotFound();
        }

        throw new InvalidOperationException("Deliberate test exception");
    }
}
```

Then create `tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/ErrorLogWritingTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Infrastructure;

public class ErrorLogWritingTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ErrorLogWritingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UnhandledBackendException_WritesErrorLogRow_AndStillReturns500()
    {
        var response = await _client.GetAsync("/api/diagnostics/throw");

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.ErrorLogs);
        Assert.Equal(ErrorSource.Backend, row.Source);
        Assert.Equal("System.InvalidOperationException", row.ExceptionType);
        Assert.Equal("/api/diagnostics/throw", row.RequestPath);
        Assert.Equal("GET", row.RequestMethod);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter ErrorLogWritingTests`
Expected: FAIL — no `ErrorLog` row is written yet.

- [ ] **Step 3: Wire the write into `ExceptionHandlingMiddleware`**

Replace the full file:

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.WebAPI.Middleware;

public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);

            var errorLogService = context.RequestServices.GetRequiredService<IErrorLogService>();
            var userId = context.RequestServices.GetRequiredService<ICurrentUserService>().UserId;
            await errorLogService.RecordAsync(
                ErrorSource.Backend, ex.GetType().FullName, ex.Message, ex.StackTrace,
                context.Request.Path, context.Request.Method, url: null, userId,
                context.Request.Headers.UserAgent.ToString(), metadata: null);

            var problem = new ProblemDetails
            {
                Status = (int)HttpStatusCode.InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment()
                    ? ex.Message
                    : null
            };

            context.Response.ContentType = "application/problem+json";
            context.Response.StatusCode = problem.Status.Value;
            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}
```

(`IErrorLogService.RecordAsync` never throws, per Task 12, so no extra try/catch is needed around
it — a logging failure here still lets the existing 500 response through unchanged.)

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter ErrorLogWritingTests`
Expected: PASS. Also run the full integration suite once to confirm the new
`/api/diagnostics/throw` route doesn't collide with anything and 404s correctly outside Testing
(covered implicitly since every other integration test runs under `Testing` already).

- [ ] **Step 5: Commit**

```bash
git add src/PSMPE.Portal.WebAPI/Middleware/ExceptionHandlingMiddleware.cs \
  src/PSMPE.Portal.WebAPI/Controllers/DiagnosticsController.cs \
  tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/ErrorLogWritingTests.cs
git commit -m "feat: record backend unhandled exceptions to ErrorLog"
```

---

## 14. Frontend error reporting endpoint

**Files:**
- Modify: `src/PSMPE.Portal.WebAPI/Extensions/RateLimitingServiceExtensions.cs`
- Create: `src/PSMPE.Portal.WebAPI/Controllers/ErrorsController.cs`
- Test: `tests/PSMPE.Portal.WebAPI.IntegrationTests/ErrorsControllerTests.cs`

- [ ] **Step 1: Add the `error-report` rate-limit policy**

In `RateLimitingServiceExtensions.cs`:

1. Add a new constant next to the existing three: `public const string ErrorReportPolicy = "error-report";`
2. Add `"ErrorReport"` to `LimitSections`: `["AuthIp", "AuthEmailSend", "UsernameProbe", "Global", "EmailSendPerAddress", "ErrorReport"];`
3. Register the policy in `AddPortalRateLimiting`, after the three existing `AddFixedWindowPolicy` calls:

```csharp
            // Necessarily unauthenticated (an error can happen before login) and accepts
            // free-text payloads - this is exactly the kind of endpoint this file exists to
            // protect. Rejections here flow through the same shared OnRejected as every other
            // policy, so they're audited too (auth.rate_limit.rejected, policy "error-report").
            AddFixedWindowPolicy(options, ErrorReportPolicy, configuration, "ErrorReport", 30, 5, enabled, knownNetworks);
```

- [ ] **Step 2: Write the failing tests**

Create `tests/PSMPE.Portal.WebAPI.IntegrationTests/ErrorsControllerTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests;

public class ErrorsControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ErrorsControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static string UniqueIp() => AuthTestHelpers.NextClientIp(secondOctet: 22);

    private record FrontendErrorReportRequest(string Message, string? StackTrace, string? Url, string? ComponentStack);

    [Fact]
    public async Task ReportFrontendError_Unauthenticated_IsAccepted_AndWritesNullUserId()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/errors/frontend")
        {
            Content = JsonContent.Create(new FrontendErrorReportRequest("Cannot read properties of undefined", "at Foo (bar.js:1:1)", "https://example.com/members", null))
        };
        request.Headers.Add("X-Forwarded-For", UniqueIp());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.ErrorLogs);
        Assert.Equal(ErrorSource.Frontend, row.Source);
        Assert.Null(row.UserId);
        Assert.Equal("https://example.com/members", row.Url);
    }

    [Fact]
    public async Task ReportFrontendError_OversizedStackTrace_IsAcceptedAndTruncated()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/errors/frontend")
        {
            Content = JsonContent.Create(new FrontendErrorReportRequest("boom", new string('s', 9000), null, null))
        };
        request.Headers.Add("X-Forwarded-For", UniqueIp());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(db.ErrorLogs);
        Assert.Equal(8000, row.StackTrace!.Length);
    }

    [Fact]
    public async Task ReportFrontendError_ExceedingRateLimit_Returns429()
    {
        var ip = UniqueIp();
        for (var i = 0; i < 31; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/errors/frontend")
            {
                Content = JsonContent.Create(new FrontendErrorReportRequest($"error {i}", null, null, null))
            };
            request.Headers.Add("X-Forwarded-For", ip);
            await _client.SendAsync(request);
        }

        var request31 = new HttpRequestMessage(HttpMethod.Post, "/api/errors/frontend")
        {
            Content = JsonContent.Create(new FrontendErrorReportRequest("one too many", null, null, null))
        };
        request31.Headers.Add("X-Forwarded-For", ip);
        var response = await _client.SendAsync(request31);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter ErrorsControllerTests`
Expected: FAIL — the route doesn't exist yet (404s).

- [ ] **Step 4: Create `ErrorsController`**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.WebAPI.Extensions;

namespace PSMPE.Portal.WebAPI.Controllers;

[ApiController]
[Route("api/errors")]
public class ErrorsController(IErrorLogService errorLogService, ICurrentUserService currentUserService) : ControllerBase
{
    public record FrontendErrorReportRequest(string Message, string? StackTrace, string? Url, string? ComponentStack);

    /// <summary>No [Authorize] - a frontend error can happen before login (e.g. on the login
    /// page itself), and this endpoint still records the caller's identity when a valid
    /// session/token is present, via ICurrentUserService reading the same JWT middleware
    /// populates for every request regardless of whether the endpoint requires it.</summary>
    [HttpPost("frontend")]
    [EnableRateLimiting(RateLimitingServiceExtensions.ErrorReportPolicy)]
    public async Task<IActionResult> ReportFrontendError(FrontendErrorReportRequest request, CancellationToken cancellationToken)
    {
        await errorLogService.RecordAsync(
            ErrorSource.Frontend, exceptionType: null, request.Message, request.StackTrace,
            requestPath: null, requestMethod: null, request.Url, currentUserService.UserId,
            Request.Headers.UserAgent.ToString(),
            metadata: request.ComponentStack is not null
                ? System.Text.Json.JsonSerializer.Serialize(new { componentStack = request.ComponentStack })
                : null,
            cancellationToken);

        return NoContent();
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter ErrorsControllerTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add src/PSMPE.Portal.WebAPI/Extensions/RateLimitingServiceExtensions.cs \
  src/PSMPE.Portal.WebAPI/Controllers/ErrorsController.cs \
  tests/PSMPE.Portal.WebAPI.IntegrationTests/ErrorsControllerTests.cs
git commit -m "feat: add rate-limited POST /api/errors/frontend"
```

---

## 15. Extend log retention to ErrorLog

**Files:**
- Modify: `src/PSMPE.Portal.Infrastructure/Services/LogRetentionService.cs`
- Test: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/LogRetentionServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `LogRetentionServiceTests.cs`:

```csharp
    [Fact]
    public async Task PruneAsync_DeletesErrorLogRowsOlderThan30Days_KeepsRecentOnes()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;

        var old = new ErrorLog { Source = ErrorSource.Backend, Message = "old", CreatedAt = now.AddDays(-31) };
        var recent = new ErrorLog { Source = ErrorSource.Frontend, Message = "recent", CreatedAt = now.AddDays(-29) };
        db.ErrorLogs.AddRange(old, recent);
        await db.SaveChangesAsync();

        var retentionService = scope.ServiceProvider.GetRequiredService<ILogRetentionService>();
        await retentionService.PruneAsync();

        var remaining = db.ErrorLogs.Select(e => e.Id).ToList();
        Assert.DoesNotContain(old.Id, remaining);
        Assert.Contains(recent.Id, remaining);
    }
```

Add `using PSMPE.Portal.Domain.Enums;` to this file's imports if not already present (from
Task 8's `AuditLog` import of `PSMPE.Portal.Domain.Entities`, which doesn't cover `ErrorSource`).

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter PruneAsync_DeletesErrorLogRows`
Expected: FAIL — `ErrorLog` rows are never pruned yet.

- [ ] **Step 3: Extend `LogRetentionService.PruneAsync`**

Replace the method body:

```csharp
    private const int AuditSecurityEventRetentionDays = 90;
    private const int ErrorLogRetentionDays = 30;

    public async Task PruneAsync(CancellationToken cancellationToken = default)
    {
        var now = dateTimeProvider.UtcNow;

        var auditCutoff = now.AddDays(-AuditSecurityEventRetentionDays);
        var staleAuditRows = await db.AuditLogs
            .Where(a => a.EventType.StartsWith("auth.") && a.CreatedAt < auditCutoff)
            .ToListAsync(cancellationToken);
        db.AuditLogs.RemoveRange(staleAuditRows);

        var errorCutoff = now.AddDays(-ErrorLogRetentionDays);
        var staleErrorRows = await db.ErrorLogs
            .Where(e => e.CreatedAt < errorCutoff)
            .ToListAsync(cancellationToken);
        db.ErrorLogs.RemoveRange(staleErrorRows);

        await db.SaveChangesAsync(cancellationToken);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter LogRetentionServiceTests`
Expected: PASS (both the Task 8 test and this one).

- [ ] **Step 5: Commit**

```bash
git add src/PSMPE.Portal.Infrastructure/Services/LogRetentionService.cs \
  tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/LogRetentionServiceTests.cs
git commit -m "feat: prune stale ErrorLog rows daily"
```

---

## 16. `IErrorLogService` query path + GET /api/admin/error-log

**Files:**
- Modify: `src/PSMPE.Portal.Application/Common/Interfaces/IErrorLogService.cs`
- Modify: `src/PSMPE.Portal.Infrastructure/Services/ErrorLogService.cs`
- Modify: `src/PSMPE.Portal.WebAPI/Controllers/SystemLogsController.cs`
- Test: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/ErrorLogServiceTests.cs`
- Test: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Admin/SystemLogsControllerTests.cs`

- [ ] **Step 1: Write the failing tests**

Add `using PSMPE.Portal.Domain.Entities;` to `ErrorLogServiceTests.cs`'s imports (needed now that
the test below constructs `ErrorLog` instances directly, unlike Task 12's tests which only ever
read them back via `var` inference). Then append to the file:

```csharp
    [Fact]
    public async Task GetPagedAsync_FiltersBySourceAndSearchesMessage()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ErrorLogs.AddRange(
            new ErrorLog { Source = ErrorSource.Backend, Message = "Null reference in MemberService" },
            new ErrorLog { Source = ErrorSource.Frontend, Message = "Cannot read properties of undefined" });
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IErrorLogService>();

        var backendOnly = await service.GetPagedAsync(1, 20, search: null, source: ErrorSource.Backend, from: null, to: null);
        Assert.Single(backendOnly.Items);

        var searched = await service.GetPagedAsync(1, 20, search: "undefined", source: null, from: null, to: null);
        Assert.Single(searched.Items);
    }
```

Update the existing `GetErrorLog_WithoutSuperAdmin_Returns403` test in `SystemLogsControllerTests.cs`
to also confirm the happy path — append a new test to that file:

```csharp
    [Fact]
    public async Task GetErrorLog_AsSuperAdmin_ResolvesUserEmail()
    {
        using var setupScope = _factory.Services.CreateScope();
        var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var (_, token) = await _client.CreatePrivilegedUserAsync(userManager, RoleNames.SuperAdmin);

        var errorUser = new ApplicationUser { UserName = "erroruser@example.com", Email = "erroruser@example.com", DisplayName = "Error User", EmailConfirmed = true };
        await userManager.CreateAsync(errorUser, "Password123!");

        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ErrorLogs.Add(new ErrorLog { Source = ErrorSource.Backend, Message = "boom", UserId = errorUser.Id });
            await db.SaveChangesAsync();
        }

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/admin/error-log").WithBearer(token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("erroruser@example.com", body.GetProperty("items")[0].GetProperty("userEmail").GetString());
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter "ErrorLogServiceTests|SystemLogsControllerTests"`
Expected: FAIL to build — `GetPagedAsync` doesn't exist on `IErrorLogService` yet, and
`GET /api/admin/error-log` isn't implemented.

- [ ] **Step 3: Add `GetPagedAsync` to `IErrorLogService`**

Append to the interface:

```csharp
    Task<PagedResult<ErrorLogDto>> GetPagedAsync(
        int page, int pageSize, string? search, ErrorSource? source, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement it in `ErrorLogService`**

Add `using Microsoft.EntityFrameworkCore;` and `using PSMPE.Portal.Application.Common.Models;` to
the top of the file if not already present, then append to the class:

```csharp
    public async Task<PagedResult<ErrorLogDto>> GetPagedAsync(
        int page, int pageSize, string? search, ErrorSource? source, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<ErrorLog> query = db.ErrorLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim().ToLower();
            query = query.Where(e =>
                e.Message.ToLower().Contains(normalized)
                || (e.ExceptionType != null && e.ExceptionType.ToLower().Contains(normalized))
                || (e.Url != null && e.Url.ToLower().Contains(normalized))
                || (e.RequestPath != null && e.RequestPath.ToLower().Contains(normalized)));
        }

        if (source is not null)
        {
            query = query.Where(e => e.Source == source);
        }

        if (from is not null)
        {
            query = query.Where(e => e.CreatedAt >= from);
        }

        if (to is not null)
        {
            query = query.Where(e => e.CreatedAt <= to);
        }

        query = query.OrderByDescending(e => e.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new ErrorLogDto(
                e.Id, e.Source, e.ExceptionType, e.Message, e.StackTrace, e.RequestPath, e.RequestMethod,
                e.Url, e.UserId, e.UserAgent, e.Metadata, e.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<ErrorLogDto>(items, totalCount, page, pageSize);
    }
```

- [ ] **Step 5: Add `GetErrorLog` to `SystemLogsController`**

Add `IErrorLogService errorLogService` to the constructor parameter list, then append below
`GetAuditLog` and its private `ResolveEmailsAsync` helper:

```csharp
    public record ErrorLogEntryDto(
        Guid Id, ErrorSource Source, string? ExceptionType, string Message, string? StackTrace,
        string? RequestPath, string? RequestMethod, string? Url, Guid? UserId, string? UserEmail,
        string? UserAgent, string? Metadata, DateTimeOffset CreatedAt);

    [HttpGet("error-log")]
    public async Task<ActionResult<PagedResult<ErrorLogEntryDto>>> GetErrorLog(
        int page = 1, int pageSize = 20, string? search = null, ErrorSource? source = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        var result = await errorLogService.GetPagedAsync(page, pageSize, search, source, from, to, cancellationToken);
        var userEmails = await ResolveEmailsAsync(result.Items.Select(e => e.UserId));

        var entries = result.Items
            .Select(e => new ErrorLogEntryDto(
                e.Id, e.Source, e.ExceptionType, e.Message, e.StackTrace, e.RequestPath, e.RequestMethod,
                e.Url, e.UserId, e.UserId is { } userId ? userEmails.GetValueOrDefault(userId) : null,
                e.UserAgent, e.Metadata, e.CreatedAt))
            .ToList();

        return Ok(new PagedResult<ErrorLogEntryDto>(entries, result.TotalCount, result.Page, result.PageSize));
    }
```

Add `using PSMPE.Portal.Domain.Enums;` to the controller's imports for `ErrorSource`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/PSMPE.Portal.WebAPI.IntegrationTests --filter "ErrorLogServiceTests|SystemLogsControllerTests"`
Expected: PASS (every test in both files).

- [ ] **Step 7: Commit**

```bash
git add src/PSMPE.Portal.Application/Common/Interfaces/IErrorLogService.cs \
  src/PSMPE.Portal.Infrastructure/Services/ErrorLogService.cs \
  src/PSMPE.Portal.WebAPI/Controllers/SystemLogsController.cs \
  tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/ErrorLogServiceTests.cs \
  tests/PSMPE.Portal.WebAPI.IntegrationTests/Admin/SystemLogsControllerTests.cs
git commit -m "feat: add GET /api/admin/error-log"
```

---

## 17. Frontend — Errors tab

**Files:**
- Modify: `apps/web/src/core/api/endpoints/systemLogsApi.ts`
- Create: `apps/web/src/integrations/template/pages/ErrorLogTable.tsx`
- Modify: `apps/web/src/integrations/template/index.ts`
- Modify: `apps/web/src/core/pages/SystemLogsPage.tsx`

- [ ] **Step 1: Extend `systemLogsApi.ts`**

Append:

```typescript
export const ErrorSource = { Backend: 0, Frontend: 1 } as const
export type ErrorSourceValue = (typeof ErrorSource)[keyof typeof ErrorSource]

export interface ErrorLogEntry {
  id: string
  source: ErrorSourceValue
  exceptionType: string | null
  message: string
  stackTrace: string | null
  requestPath: string | null
  requestMethod: string | null
  url: string | null
  userId: string | null
  userEmail: string | null
  userAgent: string | null
  metadata: string | null
  createdAt: string
}

export interface GetErrorLogParams {
  page?: number
  pageSize?: number
  search?: string
  source?: ErrorSourceValue
  from?: string
  to?: string
}
```

And add to the `systemLogsApi` object:

```typescript
  getErrorLog: (params: GetErrorLogParams = {}) =>
    apiClient.get<PagedResult<ErrorLogEntry>>('/api/admin/error-log', { params }).then((res) => res.data),
```

- [ ] **Step 2: Create `ErrorLogTable`**

Mirrors `AuditLogTable`'s shape — search, a Source filter instead of Event Type, date range,
pagination, and a details modal showing the stack trace instead of metadata:

```tsx
import { useState } from 'react'
import { ErrorSource, type ErrorLogEntry } from '../../../core/api/endpoints/systemLogsApi'
import { LogDetailsModal } from '../components/shared/LogDetailsModal'

interface ErrorLogTableProps {
  entries: ErrorLogEntry[]
  searchInput: string
  onSearchInputChange: (value: string) => void
  sourceFilter: string
  onSourceFilterChange: (value: string) => void
  from: string
  to: string
  onFromChange: (value: string) => void
  onToChange: (value: string) => void
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
}

export const ErrorLogTable = ({
  entries, searchInput, onSearchInputChange, sourceFilter, onSourceFilterChange,
  from, to, onFromChange, onToChange, page, pageSize, totalCount, onPageChange,
}: ErrorLogTableProps) => {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  const [detailsEntry, setDetailsEntry] = useState<ErrorLogEntry | null>(null)

  return (
    <div className="card">
      <div className="flex flex-wrap items-center gap-3 px-5 py-3 border-b border-default-200 bg-default-50">
        <input
          type="text"
          className="form-input max-w-xs"
          placeholder="Search message, exception type, path…"
          value={searchInput}
          onChange={(e) => onSearchInputChange(e.target.value)}
        />
        <select className="form-input max-w-xs" value={sourceFilter} onChange={(e) => onSourceFilterChange(e.target.value)}>
          <option value="">All sources</option>
          <option value={ErrorSource.Backend}>Backend</option>
          <option value={ErrorSource.Frontend}>Frontend</option>
        </select>
        <input type="date" className="form-input" value={from} onChange={(e) => onFromChange(e.target.value)} />
        <span className="text-sm text-default-500">to</span>
        <input type="date" className="form-input" value={to} onChange={(e) => onToChange(e.target.value)} />
      </div>

      <div className="flex flex-col">
        <div className="overflow-x-auto">
          <div className="min-w-full inline-block align-middle">
            <div className="overflow-hidden">
              <table className="min-w-full divide-y divide-default-200">
                <thead className="bg-default-150">
                  <tr className="text-sm font-normal text-default-700 whitespace-nowrap">
                    <th className="px-3.5 py-3 text-start">Timestamp</th>
                    <th className="px-3.5 py-3 text-start">Source</th>
                    <th className="px-3.5 py-3 text-start">Exception Type</th>
                    <th className="px-3.5 py-3 text-start">Message</th>
                    <th className="px-3.5 py-3 text-start">User</th>
                    <th className="px-3.5 py-3 text-start">Path / URL</th>
                    <th className="px-3.5 py-3 text-start">Details</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-default-200">
                  {entries.map((entry) => (
                    <tr key={entry.id} className="text-default-800 font-normal text-sm whitespace-nowrap">
                      <td className="py-3 px-3.5">{new Date(entry.createdAt).toLocaleString()}</td>
                      <td className="py-3 px-3.5">
                        <span className={`py-0.5 px-2.5 text-xs font-medium rounded ${entry.source === ErrorSource.Backend ? 'bg-primary/10 text-primary' : 'bg-warning/10 text-warning'}`}>
                          {entry.source === ErrorSource.Backend ? 'Backend' : 'Frontend'}
                        </span>
                      </td>
                      <td className="py-3 px-3.5">{entry.exceptionType ?? '—'}</td>
                      <td className="py-3 px-3.5 max-w-xs truncate">{entry.message}</td>
                      <td className="py-3 px-3.5">{entry.userEmail ?? '—'}</td>
                      <td className="py-3 px-3.5 max-w-xs truncate">{entry.requestPath ?? entry.url ?? '—'}</td>
                      <td className="py-3 px-3.5">
                        <button type="button" className="text-primary hover:underline" onClick={() => setDetailsEntry(entry)}>
                          View
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>

      <div className="card-footer flex items-center justify-between">
        <span className="text-sm text-default-500">
          Page {page} of {totalPages} ({totalCount} total)
        </span>
        <div className="flex items-center gap-1.5">
          <button type="button" className="btn btn-sm border border-default-200 disabled:opacity-50" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
            Previous
          </button>
          <button type="button" className="btn btn-sm border border-default-200 disabled:opacity-50" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}>
            Next
          </button>
        </div>
      </div>

      <LogDetailsModal
        isOpen={detailsEntry !== null}
        title="Error details"
        content={detailsEntry?.stackTrace ?? null}
        onClose={() => setDetailsEntry(null)}
      />
    </div>
  )
}
```

- [ ] **Step 3: Export it from the template barrel**

In `index.ts`, add: `export { ErrorLogTable } from './pages/ErrorLogTable'`

- [ ] **Step 4: Wire the Errors tab into `SystemLogsPage`**

In `apps/web/src/core/pages/SystemLogsPage.tsx`, change the import line to also pull in
`ErrorLogTable` and the new types:

```typescript
import { systemLogsApi, type AuditLogEntry, type ErrorLogEntry } from '../api/endpoints/systemLogsApi'
import { PageBreadcrumb, PageMeta, AuditLogTable, ErrorLogTable } from '../../integrations/template'
```

Add error-tab state next to the existing audit-tab state:

```typescript
  const [errorEntries, setErrorEntries] = useState<ErrorLogEntry[]>([])
  const [sourceFilter, setSourceFilter] = useState('')
```

Add a second data-fetching effect, symmetric to the existing audit one — both share the same
`loading`/`totalCount` state (same pattern `MembersPage` uses for its Members-vs-Payments tabs:
one shared `loading` flag, set by whichever tab's effect is currently active):

```typescript
  useEffect(() => {
    if (activeTab !== 'errors') return
    let cancelled = false
    setLoading(true)
    systemLogsApi
      .getErrorLog({
        page, pageSize: PAGE_SIZE,
        ...(search ? { search } : {}),
        ...(sourceFilter ? { source: Number(sourceFilter) as 0 | 1 } : {}),
        ...(from ? { from } : {}),
        ...(to ? { to } : {}),
      })
      .then((result) => {
        if (cancelled) return
        setErrorEntries(result.items)
        setTotalCount(result.totalCount)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [activeTab, page, search, sourceFilter, from, to])
```

Replace the render branch:

```tsx
          activeTab === 'audit' && (
            <AuditLogTable
              entries={auditEntries}
              searchInput={searchInput}
              onSearchInputChange={setSearchInput}
              eventTypeFilter={eventTypeFilter}
              onEventTypeFilterChange={(value) => {
                setEventTypeFilter(value)
                setPage(1)
              }}
              from={from}
              to={to}
              onFromChange={(value) => {
                setFrom(value)
                setPage(1)
              }}
              onToChange={(value) => {
                setTo(value)
                setPage(1)
              }}
              page={page}
              pageSize={PAGE_SIZE}
              totalCount={totalCount}
              onPageChange={setPage}
            />
          )
```

with:

```tsx
          activeTab === 'audit' ? (
            <AuditLogTable
              entries={auditEntries}
              searchInput={searchInput}
              onSearchInputChange={setSearchInput}
              eventTypeFilter={eventTypeFilter}
              onEventTypeFilterChange={(value) => {
                setEventTypeFilter(value)
                setPage(1)
              }}
              from={from}
              to={to}
              onFromChange={(value) => {
                setFrom(value)
                setPage(1)
              }}
              onToChange={(value) => {
                setTo(value)
                setPage(1)
              }}
              page={page}
              pageSize={PAGE_SIZE}
              totalCount={totalCount}
              onPageChange={setPage}
            />
          ) : (
            <ErrorLogTable
              entries={errorEntries}
              searchInput={searchInput}
              onSearchInputChange={setSearchInput}
              sourceFilter={sourceFilter}
              onSourceFilterChange={(value) => {
                setSourceFilter(value)
                setPage(1)
              }}
              from={from}
              to={to}
              onFromChange={(value) => {
                setFrom(value)
                setPage(1)
              }}
              onToChange={(value) => {
                setTo(value)
                setPage(1)
              }}
              page={page}
              pageSize={PAGE_SIZE}
              totalCount={totalCount}
              onPageChange={setPage}
            />
          )
```

`handleTabChange` already resets `page` to 1 (Task 11), so switching tabs never carries over a
stale page number. The shared `searchInput`/`search`/`from`/`to` state resets naturally too,
since typing a new search term or date always re-triggers whichever tab's effect is active.

- [ ] **Step 5: Verify**

Run: `npx tsc -b` and `npx eslint .` (from `apps/web`) — expected: no errors.

- [ ] **Step 6: Manual browser verification**

- Trigger `/api/diagnostics/throw` isn't available outside Testing, so instead: temporarily break
  something (or wait for Task 18's frontend capture) to generate a real Backend error row, and
  confirm it appears on the Errors tab with working search/source filter/date range/pagination
  and a working stack-trace details modal.

- [ ] **Step 7: Commit**

```bash
git add apps/web/src/core/api/endpoints/systemLogsApi.ts \
  apps/web/src/integrations/template/pages/ErrorLogTable.tsx \
  apps/web/src/integrations/template/index.ts \
  apps/web/src/core/pages/SystemLogsPage.tsx
git commit -m "feat: add Errors tab to System Logs page"
```

---

## 18. Frontend error capture (React error boundary + global handlers)

**Files:**
- Create: `apps/web/src/core/errorReporting/reportError.ts`
- Create: `apps/web/src/core/errorReporting/AppErrorBoundary.tsx`
- Create: `apps/web/src/core/errorReporting/setupGlobalErrorHandlers.ts`
- Modify: `apps/web/src/App.tsx`
- Modify: `apps/web/src/main.tsx`

- [ ] **Step 1: Create `reportError.ts`**

Uses plain `fetch`, not the shared `apiClient`/axios instance — an error captured while axios
itself is misbehaving must not recurse back through axios's own interceptor stack (see
`apiClient.ts`'s 401 interceptor). Swallows its own failures; this is the last line of defense
and must never throw.

```typescript
import { API_BASE_URL, tokenStorage } from '../api/apiClient'

interface FrontendErrorReport {
  message: string
  stackTrace?: string
  url?: string
  componentStack?: string
}

export function reportError(report: FrontendErrorReport) {
  const token = tokenStorage.get()
  fetch(`${API_BASE_URL}/api/errors/frontend`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: JSON.stringify({
      message: report.message,
      stackTrace: report.stackTrace ?? null,
      url: report.url ?? window.location.href,
      componentStack: report.componentStack ?? null,
    }),
  }).catch(() => {
    // Best-effort - if the report itself fails to send, there is nowhere left to report that.
  })
}
```

- [ ] **Step 2: Create `AppErrorBoundary`**

```tsx
import { Component, type ErrorInfo, type ReactNode } from 'react'
import { reportError } from './reportError'

interface AppErrorBoundaryProps {
  children: ReactNode
}

interface AppErrorBoundaryState {
  hasError: boolean
}

export class AppErrorBoundary extends Component<AppErrorBoundaryProps, AppErrorBoundaryState> {
  state: AppErrorBoundaryState = { hasError: false }

  static getDerivedStateFromError(): AppErrorBoundaryState {
    return { hasError: true }
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    reportError({ message: error.message, stackTrace: error.stack, componentStack: errorInfo.componentStack ?? undefined })
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="flex items-center justify-center min-h-screen p-6 text-center">
          <div>
            <h1 className="text-xl font-semibold text-default-900 mb-2">Something went wrong</h1>
            <p className="text-sm text-default-600 mb-4">
              The page ran into an unexpected error. Try reloading.
            </p>
            <button type="button" className="btn btn-sm bg-primary text-white" onClick={() => window.location.reload()}>
              Reload
            </button>
          </div>
        </div>
      )
    }

    return this.props.children
  }
}
```

- [ ] **Step 3: Create `setupGlobalErrorHandlers.ts`**

```typescript
import { reportError } from './reportError'

/** Catches errors React's own boundary can't - runtime errors outside render (event handlers,
 *  timers) and unhandled promise rejections. Call once, at app bootstrap. */
export function setupGlobalErrorHandlers() {
  window.addEventListener('error', (event) => {
    reportError({ message: event.message, stackTrace: event.error?.stack })
  })

  window.addEventListener('unhandledrejection', (event) => {
    const reason = event.reason
    reportError({
      message: reason instanceof Error ? reason.message : String(reason),
      stackTrace: reason instanceof Error ? reason.stack : undefined,
    })
  })
}
```

- [ ] **Step 4: Wire the error boundary into `App.tsx`**

```tsx
import 'flatpickr/dist/flatpickr.css'
import { RouterProvider } from 'react-router-dom'
import { AuthProvider } from './core/auth/AuthContext'
import { router } from './core/routes/router'
import { AppErrorBoundary } from './core/errorReporting/AppErrorBoundary'

export function App() {
  return (
    <AppErrorBoundary>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </AppErrorBoundary>
  )
}
```

- [ ] **Step 5: Wire the global handlers into `main.tsx`**

```tsx
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles/index.css'
import { App } from './App.tsx'
import { setupGlobalErrorHandlers } from './core/errorReporting/setupGlobalErrorHandlers'

setupGlobalErrorHandlers()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
```

- [ ] **Step 6: Verify**

Run: `npx tsc -b` and `npx eslint .` (from `apps/web`) — expected: no errors.

- [ ] **Step 7: Manual browser verification**

- Temporarily throw inside a component's render (e.g. `throw new Error('test')` in a page
  component) and confirm the fallback UI renders instead of a blank screen, and a Frontend
  `ErrorLog` row appears in System Logs → Errors with a component stack in its details modal.
  Revert the deliberate throw afterward.
- Trigger a plain runtime error in a `setTimeout`/event handler and confirm it's also captured.
- Trigger a rejected promise with no `.catch` and confirm it's captured too.
- Confirm none of this breaks normal navigation/usage of the app.

- [ ] **Step 8: Commit**

```bash
git add apps/web/src/core/errorReporting apps/web/src/App.tsx apps/web/src/main.tsx
git commit -m "feat: add frontend error boundary and global error capture"
```

---

## 19. Final verification and docs

**Files:**
- Modify: `openspec/changes/add-audit-and-error-logs/proposal.md`
- Modify: `openspec/changes/add-audit-and-error-logs/tasks.md` (this file)
- Modify: `openspecs/auth.md` and/or a new `openspecs/system-logs.md`, per this repo's convention
  of keeping the "docs in sync with shipped code" layer current (see commit `41486b2`,
  `3005621`)

- [ ] **Step 1: Full backend verification**

```bash
dotnet build src/PSMPE.Portal.sln
dotnet test src/PSMPE.Portal.sln --no-build
```
Expected: 0 build errors/warnings-as-errors, all tests pass (the full suite, not just this
feature's — confirms no regression anywhere touched: rate limiting, auth, members).

- [ ] **Step 2: Full frontend verification**

From `apps/web`:
```bash
npx tsc -b
npx eslint .
npm run build
```
Expected: all three succeed.

- [ ] **Step 3: Full manual browser pass**

Re-run every "Manual browser verification" step listed in Tasks 11, 17, and 18 in one sitting
against a freshly built app, since earlier passes were done incrementally against partial UI.

- [ ] **Step 4: Update `openspecs/` living docs**

Document the new `GET /api/admin/audit-log`, `GET /api/admin/error-log`, and
`POST /api/errors/frontend` endpoints (params, auth requirements, response shape) in whichever
`openspecs/*.md` file this repo's convention points to for admin/system endpoints — follow the
exact pattern `3005621` used for the Members/Users search-filter endpoints.

- [ ] **Step 5: Flip `proposal.md`'s Status to Implemented**

Replace the `## Status` section's `**Proposed.**` line with an `**Implemented.**` summary
(test counts, build/lint status), following the exact convention already used in
`openspec/changes/add-members-users-search-filter/proposal.md`'s Status section.

- [ ] **Step 6: Check off every completed box in this file**

Go through `tasks.md` top to bottom and confirm every `- [ ]` that was actually completed is now
`- [x]`.

- [ ] **Step 7: Commit**

```bash
git add openspec/changes/add-audit-and-error-logs openspecs
git commit -m "docs: mark add-audit-and-error-logs implemented"
```
