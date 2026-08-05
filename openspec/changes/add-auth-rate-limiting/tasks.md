# Tasks: add-auth-rate-limiting

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Protect the public auth surface with IP-partitioned rate limiting, per-account lockout, and per-address email throttling, on a client-IP trust chain that currently does not exist.

**Architecture:** Three mechanisms separated by what each can see — `Microsoft.AspNetCore.RateLimiting` fixed-window policies partitioned on client IP; ASP.NET Identity lockout for per-account brute force; an `IMemoryCache`-backed per-address throttle in the controller for email sends. All of it depends on `UseForwardedHeaders` plus nginx actually forwarding the client IP, which Task 1 establishes first.

**Tech Stack:** .NET 8 (`Microsoft.AspNetCore.RateLimiting` and `System.Threading.RateLimiting` are in-framework — no NuGet package), ASP.NET Core Identity, xUnit + `WebApplicationFactory` with EF Core InMemory, React + axios, nginx, Docker Compose.

**Before starting:** read `proposal.md` and `specs/auth/spec.md` in this folder.

---

## 1. Client IP trust chain

Nothing else in this plan works until the app can see a real client IP. Do this first.

**Files:**
- Modify: `src/PSMPE.Portal.WebAPI/Program.cs`
- Create: `tests/PSMPE.Portal.WebAPI.IntegrationTests/TestSupport/FakeRemoteIpStartupFilter.cs`
- Modify: `tests/PSMPE.Portal.WebAPI.IntegrationTests/CustomWebApplicationFactory.cs`
- Create: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/ForwardedHeadersTests.cs`

- [ ] **1.1 Add the test-side remote IP filter**

`TestServer` leaves `HttpContext.Connection.RemoteIpAddress` null, and `ForwardedHeadersMiddleware` **skips its known-peer check entirely when the peer is null** (its guard reads `RemoteIpAndPort != null && checkKnownIps && !CheckKnownAddress(...)`, deliberately allowing null "for servers that don't support it natively"). So a null peer is maximally *trusted*, not untrusted — without this filter the trusted-path tests would pass through that bypass rather than through the real `KnownNetworks` check, and the untrusted-peer test could not be written at all. This filter stands in for the Docker bridge gateway hop present in every real deployment. `IStartupFilter` middleware runs *before* the pipeline in `Program.cs`, which is exactly where a real proxy hop sits.

Create `tests/PSMPE.Portal.WebAPI.IntegrationTests/TestSupport/FakeRemoteIpStartupFilter.cs`:

```csharp
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;

/// <summary>
/// TestServer never sets Connection.RemoteIpAddress, so ForwardedHeaders would treat every
/// request as coming from an untrusted peer and discard X-Forwarded-For. This stands in for
/// the Docker bridge gateway hop that exists in every real deployment, letting tests drive
/// the genuine forwarded-header code path instead of mocking around it.
/// </summary>
public class FakeRemoteIpStartupFilter : IStartupFilter
{
    public const string ProxyPeer = "172.17.0.1";
    public const string UntrustedPeer = "203.0.113.250";

    /// <summary>Set per-request via the X-Test-Peer header; defaults to the trusted proxy peer.</summary>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                var peer = context.Request.Headers.TryGetValue("X-Test-Peer", out var value) && !string.IsNullOrWhiteSpace(value)
                    ? value.ToString()
                    : ProxyPeer;
                context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
                await nextMiddleware();
            });
            next(app);
        };
}
```

- [ ] **1.2 Register the filter and rate-limit test config in the factory**

In `tests/PSMPE.Portal.WebAPI.IntegrationTests/CustomWebApplicationFactory.cs`, add to the `AddInMemoryCollection` dictionary (after the existing `["OpenAI:ApiKey"]` line, adding a comma to it):

```csharp
                ["OpenAI:ApiKey"] = "test-key-not-used",
                ["RateLimit:Enabled"] = "true",
                ["ForwardedHeaders:KnownNetworks"] = "172.16.0.0/12"
```

And inside the existing `builder.ConfigureServices(services => { ... })` block, after the `AddDbContext` line:

```csharp
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter,
                TestSupport.FakeRemoteIpStartupFilter>();
```

- [ ] **1.3 Write the failing forwarded-headers tests**

Create `tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/ForwardedHeadersTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.WebAPI.IntegrationTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Infrastructure;

public class ForwardedHeadersTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ForwardedHeadersTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> AdminTokenAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var (_, token) = await _client.CreatePrivilegedUserAsync(userManager, RoleNames.Admin);
        return token;
    }

    private async Task<string> ResolvedClientIpAsync(string? forwardedFor, string? testPeer = null)
    {
        var token = await AdminTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/diagnostics/client-ip").WithBearer(token);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }
        if (testPeer is not null)
        {
            request.Headers.Add("X-Test-Peer", testPeer);
        }

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return body.GetProperty("clientIp").GetString()!;
    }

    [Fact]
    public async Task ClientIp_FromTrustedProxy_IsTakenFromForwardedHeader()
    {
        Assert.Equal("203.0.113.7", await ResolvedClientIpAsync("203.0.113.7"));
    }

    [Fact]
    public async Task ClientIp_FromUntrustedPeer_IgnoresForwardedHeader()
    {
        var resolved = await ResolvedClientIpAsync("198.51.100.1", FakeRemoteIpStartupFilter.UntrustedPeer);
        Assert.Equal(FakeRemoteIpStartupFilter.UntrustedPeer, resolved);
    }

    [Fact]
    public async Task ClientIp_WithAppendedChain_UsesRightmostEntry()
    {
        // What nginx's $proxy_add_x_forwarded_for actually produces when a client sends its own
        // header: client value first, real peer appended last. Only the last entry is trustworthy.
        Assert.Equal("203.0.113.7", await ResolvedClientIpAsync("198.51.100.1, 203.0.113.7"));
    }

    [Fact]
    public async Task ClientIpDiagnostics_ForNonAdmin_IsRefused()
    {
        var memberToken = await _client.RegisterAndLoginAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/diagnostics/client-ip")
            .WithBearer(memberToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```

- [ ] **1.4 Run the tests to verify they fail**

Run: `dotnet test src/PSMPE.Portal.sln --filter ForwardedHeadersTests`
Expected: FAIL — 404 on `/api/admin/diagnostics/client-ip` (the endpoint arrives in Task 5), so the `Assert.Equal(HttpStatusCode.OK, ...)` assertion fails. `ClientIpDiagnostics_ForNonAdmin_IsRefused` also fails, expecting 403 but getting 404.

> These three stay red until Task 5. That is deliberate: the diagnostics endpoint exists to prove this chain, so its tests belong to the chain. Do not implement the endpoint here.

- [ ] **1.5 Add forwarded headers to the pipeline**

In `src/PSMPE.Portal.WebAPI/Program.cs`, add to the usings at the top:

```csharp
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
```

Immediately after `builder.Services.AddHealthChecks();`, add:

```csharp
// The app sits behind nginx, which is the only thing that ever talks to Kestrel directly.
// Without this, every request appears to come from the Docker bridge gateway and every
// IP-partitioned rate limit collapses into a single global bucket.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // Defaults trust loopback only. nginx proxies to localhost:5000, which is docker-proxy, so
    // the container sees the bridge gateway (172.x.x.1) instead - the default would silently
    // reject the header.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    var cidrs = builder.Configuration["ForwardedHeaders:KnownNetworks"] ?? "172.16.0.0/12";
    foreach (var cidr in cidrs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var parts = cidr.Split('/');
        options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse(parts[0]), int.Parse(parts[1])));
    }

    // Exactly one hop. nginx's $proxy_add_x_forwarded_for APPENDS the real peer to whatever the
    // client sent, so the rightmost entry is the only trustworthy one. Raising this would let an
    // attacker pick their own rate limit partition with a forged header.
    options.ForwardLimit = 1;
});
```

Then, in the pipeline section, make `UseForwardedHeaders` the **first** middleware — insert it directly above `app.UseMiddleware<ExceptionHandlingMiddleware>();`:

```csharp
// First in the pipeline: everything downstream (CORS, auth, rate limiting, logging) should
// see the real client address, not the proxy's.
app.UseForwardedHeaders();
```

- [ ] **1.6 Commit**

```bash
git add src/PSMPE.Portal.WebAPI/Program.cs tests/PSMPE.Portal.WebAPI.IntegrationTests/TestSupport/FakeRemoteIpStartupFilter.cs tests/PSMPE.Portal.WebAPI.IntegrationTests/CustomWebApplicationFactory.cs tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/ForwardedHeadersTests.cs
git commit -m "Resolve client IP from X-Forwarded-For behind nginx"
```

---

## 2. Rate limiter policies and the 429 contract

**Files:**
- Create: `src/PSMPE.Portal.WebAPI/Extensions/RateLimitingServiceExtensions.cs`
- Modify: `src/PSMPE.Portal.WebAPI/Program.cs`
- Modify: `src/PSMPE.Portal.WebAPI/Controllers/AuthController.cs`
- Create: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/RateLimitingTests.cs`

- [ ] **2.1 Write the failing rate limiting tests**

Create `tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/RateLimitingTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using PSMPE.Portal.Application.Auth;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Infrastructure;

public class RateLimitingTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RateLimitingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Limiter state is process-wide and the factory is shared across the class, so every test
    /// must claim its own partition or tests pollute each other's counters and fail by run order.
    /// </summary>
    private static string UniqueIp() => $"198.51.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 255)}";

    private async Task<HttpResponseMessage> PostLoginAsync(string clientIp)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest($"{Guid.NewGuid()}@example.com", "Password123!"))
        };
        request.Headers.Add("X-Forwarded-For", clientIp);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Login_WithinTheLimit_IsNotThrottled()
    {
        var ip = UniqueIp();
        for (var i = 0; i < 20; i++)
        {
            var response = await PostLoginAsync(ip);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Fact]
    public async Task Login_BeyondTheLimit_Returns429WithProblemDetailsAndRetryAfter()
    {
        var ip = UniqueIp();
        for (var i = 0; i < 20; i++)
        {
            await PostLoginAsync(ip);
        }

        var response = await PostLoginAsync(ip);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.Contains("Retry-After"), "Retry-After header must be present");

        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(429, body.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Login_LimitsArePartitionedByClientIp()
    {
        var exhausted = UniqueIp();
        for (var i = 0; i < 21; i++)
        {
            await PostLoginAsync(exhausted);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostLoginAsync(exhausted)).StatusCode);

        var fresh = await PostLoginAsync(UniqueIp());
        Assert.NotEqual(HttpStatusCode.TooManyRequests, fresh.StatusCode);
    }

    [Fact]
    public async Task UsernameAvailable_ToleratesTypeaheadVolume()
    {
        var ip = UniqueIp();
        for (var i = 0; i < 15; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/username-available?username=probe{i}");
            request.Headers.Add("X-Forwarded-For", ip);
            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task RateLimiting_WhenDisabled_NeverThrottles()
    {
        // The kill switch has to actually kill: if limiting ever throttles the wrong people in
        // production, flipping this env var is the rollback, so it needs to be known to work.
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimit:Enabled"] = "false"
                })));
        using var client = factory.CreateClient();

        var ip = UniqueIp();
        for (var i = 0; i < 25; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest($"{Guid.NewGuid()}@example.com", "Password123!"))
            };
            request.Headers.Add("X-Forwarded-For", ip);
            var response = await client.SendAsync(request);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }
}
```

Add these usings at the top of the file alongside the others:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
```

- [ ] **2.2 Run the tests to verify they fail**

Run: `dotnet test src/PSMPE.Portal.sln --filter RateLimitingTests`
Expected: FAIL — `Login_BeyondTheLimit_...` and `Login_LimitsArePartitionedByClientIp` fail because nothing returns 429. The other three pass already (nothing is limited yet); they are regression guards for the limits being set too tight and for the kill switch working.

- [ ] **2.3 Add the rate limiting extension**

Create `src/PSMPE.Portal.WebAPI/Extensions/RateLimitingServiceExtensions.cs`:

```csharp
using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace PSMPE.Portal.WebAPI.Extensions;

/// <summary>
/// Fixed-window limits on the public auth surface, partitioned by resolved client IP (see
/// UseForwardedHeaders in Program.cs - without it every partition key is the Docker bridge
/// gateway and all traffic shares one bucket).
///
/// Fixed window rather than sliding/token bucket: one counter per partition instead of a
/// timestamp log, and the limits are loose enough that a 2x burst across a window edge doesn't
/// matter. Per-account defenses live elsewhere (Identity lockout, the per-address email
/// throttle) because a limiter partitioned on IP can't see accounts, and because members
/// sharing an office IP would otherwise be throttled collectively.
/// </summary>
public static class RateLimitingServiceExtensions
{
    public const string AuthIpPolicy = "auth-ip";
    public const string AuthEmailSendPolicy = "auth-email-send";
    public const string UsernameProbePolicy = "username-probe";

    private static int _proxyIpWarningLogged;

    public static IServiceCollection AddPortalRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var enabled = configuration.GetValue<bool?>("RateLimit:Enabled") ?? true;
        var knownNetworks = ParseKnownNetworks(configuration);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            AddFixedWindowPolicy(options, AuthIpPolicy, configuration, "AuthIp", 20, 5, enabled, knownNetworks);
            AddFixedWindowPolicy(options, AuthEmailSendPolicy, configuration, "AuthEmailSend", 10, 60, enabled, knownNetworks);
            AddFixedWindowPolicy(options, UsernameProbePolicy, configuration, "UsernameProbe", 30, 1, enabled, knownNetworks);

            // Applies on top of the endpoint policies above, as a blanket ceiling on everything else.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!enabled)
                {
                    return RateLimitPartition.GetNoLimiter("disabled");
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    ClientIpPartitionKey(context, knownNetworks),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = configuration.GetValue<int?>("RateLimit:Global:PermitLimit") ?? 300,
                        Window = TimeSpan.FromMinutes(configuration.GetValue<int?>("RateLimit:Global:WindowMinutes") ?? 1),
                        QueueLimit = 0
                    });
            });

            // Matches ExceptionHandlingMiddleware's shape so the API has one error contract.
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
                }

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests.",
                    Detail = "You've made too many requests in a short period. Please wait and try again."
                };

                context.HttpContext.Response.StatusCode = problem.Status.Value;
                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
            };
        });

        return services;
    }

    private static void AddFixedWindowPolicy(
        RateLimiterOptions options,
        string policyName,
        IConfiguration configuration,
        string configSection,
        int defaultPermitLimit,
        int defaultWindowMinutes,
        bool enabled,
        IPNetwork[] knownNetworks)
    {
        options.AddPolicy(policyName, context =>
        {
            if (!enabled)
            {
                return RateLimitPartition.GetNoLimiter("disabled");
            }

            return RateLimitPartition.GetFixedWindowLimiter(
                $"{policyName}:{ClientIpPartitionKey(context, knownNetworks)}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = configuration.GetValue<int?>($"RateLimit:{configSection}:PermitLimit") ?? defaultPermitLimit,
                    Window = TimeSpan.FromMinutes(
                        configuration.GetValue<int?>($"RateLimit:{configSection}:WindowMinutes") ?? defaultWindowMinutes),
                    QueueLimit = 0
                });
        });
    }

    private static IPNetwork[] ParseKnownNetworks(IConfiguration configuration)
    {
        var cidrs = configuration["ForwardedHeaders:KnownNetworks"] ?? "172.16.0.0/12";
        return cidrs
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(IPNetwork.Parse)
            .ToArray();
    }

    /// <summary>
    /// A resolved IP still inside the proxy network means X-Forwarded-For isn't arriving, which
    /// would silently put every caller in one bucket and look like "the whole site is throttled".
    /// Warn once per process rather than per request.
    /// </summary>
    private static string ClientIpPartitionKey(HttpContext context, IPNetwork[] knownNetworks)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip is null)
        {
            return "unknown";
        }

        if (knownNetworks.Any(network => network.Contains(ip))
            && Interlocked.Exchange(ref _proxyIpWarningLogged, 1) == 0)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("RateLimiting")
                .LogWarning(
                    "Resolved client IP {ClientIp} is inside a known-proxy network - X-Forwarded-For is probably not being set by nginx. Every request is sharing one rate limit partition.",
                    ip);
        }

        return ip.ToString();
    }
}
```

> `System.Net.IPNetwork` (used for the warn-once check) and `Microsoft.AspNetCore.HttpOverrides.IPNetwork` (used by `ForwardedHeadersOptions` in Task 1.5) are different types with the same name. This file uses only `System.Net`; `Program.cs` uses only `HttpOverrides`. Don't add the other `using` to either file.

- [ ] **2.4 Wire the limiter into the pipeline**

In `src/PSMPE.Portal.WebAPI/Program.cs`, after `builder.Services.AddPortalSwagger();`:

```csharp
builder.Services.AddPortalRateLimiting(builder.Configuration);
```

And in the pipeline, between `app.UseAuthorization();` and `app.MapControllers();`:

```csharp
app.UseRateLimiter();
```

- [ ] **2.5 Apply the policies to the auth endpoints**

In `src/PSMPE.Portal.WebAPI/Controllers/AuthController.cs`, add to the usings:

```csharp
using Microsoft.AspNetCore.RateLimiting;
using PSMPE.Portal.WebAPI.Extensions;
```

Add one attribute per endpoint, directly beneath each existing `[HttpPost]`/`[HttpGet]` line:

| Line | Existing attribute | Add beneath it |
|---|---|---|
| ~70 | `[HttpPost("register")]` | `[EnableRateLimiting(RateLimitingServiceExtensions.AuthIpPolicy)]` |
| ~139 | `[HttpPost("verify-email")]` | `[EnableRateLimiting(RateLimitingServiceExtensions.AuthIpPolicy)]` |
| ~160 | `[HttpPost("resend-verification-email")]` | `[EnableRateLimiting(RateLimitingServiceExtensions.AuthEmailSendPolicy)]` |
| ~182 | `[HttpPost("forgot-password")]` | `[EnableRateLimiting(RateLimitingServiceExtensions.AuthEmailSendPolicy)]` |
| ~206 | `[HttpPost("reset-password")]` | `[EnableRateLimiting(RateLimitingServiceExtensions.AuthIpPolicy)]` |
| ~289 | `[HttpGet("username-available")]` | `[EnableRateLimiting(RateLimitingServiceExtensions.UsernameProbePolicy)]` |
| ~301 | `[HttpPost("login")]` | `[EnableRateLimiting(RateLimitingServiceExtensions.AuthIpPolicy)]` |

- [ ] **2.6 Run the tests to verify they pass**

Run: `dotnet test src/PSMPE.Portal.sln --filter RateLimitingTests`
Expected: PASS — 5 passed.

- [ ] **2.7 Run the full suite for regressions**

Run: `dotnet test src/PSMPE.Portal.sln`
Expected: PASS. `AuthControllerTests` shares the `auth-ip` partition (all its requests resolve to the same test peer), so if any test now 429s, that is the shared-partition problem the plan warns about — give `AuthControllerTests` requests an `X-Forwarded-For` of their own rather than raising the limit.

- [ ] **2.8 Commit**

```bash
git add src/PSMPE.Portal.WebAPI/Extensions/RateLimitingServiceExtensions.cs src/PSMPE.Portal.WebAPI/Program.cs src/PSMPE.Portal.WebAPI/Controllers/AuthController.cs tests/PSMPE.Portal.WebAPI.IntegrationTests/Infrastructure/RateLimitingTests.cs
git commit -m "Rate limit the public auth endpoints by client IP"
```

---

## 3. Account lockout

**Files:**
- Modify: `src/PSMPE.Portal.Infrastructure/DependencyInjection.cs:31-36`
- Modify: `src/PSMPE.Portal.WebAPI/Controllers/AuthController.cs:301-319`
- Create: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Auth/AccountLockoutTests.cs`

- [ ] **3.1 Write the failing lockout tests**

Create `tests/PSMPE.Portal.WebAPI.IntegrationTests/Auth/AccountLockoutTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using PSMPE.Portal.Application.Auth;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Auth;

public class AccountLockoutTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AccountLockoutTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static string UniqueIp() => $"198.51.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 255)}";

    /// <summary>Registers and verifies, so the account is loginable and lockout is what fails it.</summary>
    private async Task<string> VerifiedAccountAsync()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var register = await SendAsync(HttpMethod.Post, "/api/auth/register",
            new RegisterRequest(email, "Password123!", "Lockout Tester", DataPrivacyConsent: true), UniqueIp());
        var body = await register.Content.ReadFromJsonAsync<RegisterResponse>();

        var uri = new Uri(body!.DevVerificationLink!);
        var query = QueryHelpers.ParseQuery(uri.Query);
        await SendAsync(HttpMethod.Post, "/api/auth/verify-email",
            new VerifyEmailRequest(Guid.Parse(query["userId"]!), query["token"]!), UniqueIp());

        return email;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object payload, string clientIp)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-Forwarded-For", clientIp);
        return await _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password, string? clientIp = null) =>
        SendAsync(HttpMethod.Post, "/api/auth/login", new LoginRequest(email, password), clientIp ?? UniqueIp());

    [Fact]
    public async Task Login_AfterFiveFailures_LocksTheAccount()
    {
        var email = await VerifiedAccountAsync();
        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(email, "WrongPassword1!");
        }

        var response = await LoginAsync(email, "WrongPassword1!");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("ACCOUNT_LOCKED", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_LockoutHoldsAcrossDifferentClientIps()
    {
        var email = await VerifiedAccountAsync();

        // The whole point of lockout: rotating IPs defeats the per-IP limiter but not this.
        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(email, "WrongPassword1!", UniqueIp());
        }

        var response = await LoginAsync(email, "WrongPassword1!", UniqueIp());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("ACCOUNT_LOCKED", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_SuccessfulAttempt_ResetsTheFailureCount()
    {
        var email = await VerifiedAccountAsync();
        for (var i = 0; i < 4; i++)
        {
            await LoginAsync(email, "WrongPassword1!");
        }

        var good = await LoginAsync(email, "Password123!");
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);

        // If the count had survived, four more failures would lock the account.
        for (var i = 0; i < 4; i++)
        {
            var response = await LoginAsync(email, "WrongPassword1!");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Login_AgainstUnknownEmail_StaysGenericAndDoesNotLeakExistence()
    {
        var unknown = $"{Guid.NewGuid()}@example.com";
        for (var i = 0; i < 6; i++)
        {
            var response = await LoginAsync(unknown, "WrongPassword1!");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
```

- [ ] **3.2 Run the tests to verify they fail**

Run: `dotnet test src/PSMPE.Portal.sln --filter AccountLockoutTests`
Expected: FAIL — `Login_AfterFiveFailures_LocksTheAccount` and `Login_LockoutHoldsAcrossDifferentClientIps` return 401 instead of 403. The other two pass already and are regression guards.

- [ ] **3.3 Configure lockout**

In `src/PSMPE.Portal.Infrastructure/DependencyInjection.cs`, replace the `AddIdentity` options block (lines 31-36) with:

```csharp
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;

                // Per-account, so an attacker rotating IPs to defeat the per-IP rate limiter
                // gains nothing. The LockoutEnd/LockoutEnabled columns already exist (Identity
                // created them in InitialCreate) - nothing wrote to them until now, so this
                // needs no migration.
                options.Lockout.MaxFailedAccessAttempts =
                    configuration.GetValue<int?>("Lockout:MaxFailedAttempts") ?? 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(
                    configuration.GetValue<int?>("Lockout:MinutesLockedOut") ?? 15);
                options.Lockout.AllowedForNewUsers = true;
            })
```

- [ ] **3.4 Rewrite Login to record failures**

In `src/PSMPE.Portal.WebAPI/Controllers/AuthController.cs`, replace the body of `Login` (lines 301-319) with:

```csharp
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitingServiceExtensions.AuthIpPolicy)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        const string genericFailure = "Invalid email or password.";
        const string lockedMessage = "This account is temporarily locked after too many failed sign-in attempts. Please try again later.";

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            // Same response as a wrong password - never reveal whether the account exists.
            return Unauthorized(new { message = genericFailure });
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return StatusCode(403, new { message = lockedMessage, code = "ACCOUNT_LOCKED" });
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            if (await userManager.IsLockedOutAsync(user))
            {
                return StatusCode(403, new { message = lockedMessage, code = "ACCOUNT_LOCKED" });
            }

            return Unauthorized(new { message = genericFailure });
        }

        await userManager.ResetAccessFailedCountAsync(user);

        if (!user.EmailConfirmed)
        {
            return StatusCode(403, new { message = "Please verify your email before signing in.", code = "EMAIL_NOT_CONFIRMED" });
        }

        var roles = await userManager.GetRolesAsync(user);
        var permissions = await GetPermissionsAsync(roles);
        var (token, expiresAt) = jwtTokenGenerator.GenerateToken(user, roles, permissions);
        return Ok(new AuthResponse(token, expiresAt, user.Email!, user.DisplayName, roles.ToList()));
    }
```

- [ ] **3.5 Run the tests to verify they pass**

Run: `dotnet test src/PSMPE.Portal.sln --filter AccountLockoutTests`
Expected: PASS — 4 passed.

- [ ] **3.6 Run the full suite**

Run: `dotnet test src/PSMPE.Portal.sln`
Expected: PASS.

- [ ] **3.7 Commit**

```bash
git add src/PSMPE.Portal.Infrastructure/DependencyInjection.cs src/PSMPE.Portal.WebAPI/Controllers/AuthController.cs tests/PSMPE.Portal.WebAPI.IntegrationTests/Auth/AccountLockoutTests.cs
git commit -m "Lock accounts after repeated failed sign-in attempts"
```

---

## 4. Per-address email send throttle

**Files:**
- Create: `src/PSMPE.Portal.Application/Common/Interfaces/IEmailSendThrottle.cs`
- Create: `src/PSMPE.Portal.Infrastructure/Services/MemoryCacheEmailSendThrottle.cs`
- Modify: `src/PSMPE.Portal.Infrastructure/DependencyInjection.cs`
- Modify: `src/PSMPE.Portal.WebAPI/Controllers/AuthController.cs`
- Create: `tests/PSMPE.Portal.WebAPI.IntegrationTests/Auth/EmailSendThrottleTests.cs`

- [ ] **4.1 Write the failing throttle tests**

`ForgotPasswordResponse` carries a dev-only reset link outside production, so a suppressed send is observable as a null link — no email-sender test double needed.

Create `tests/PSMPE.Portal.WebAPI.IntegrationTests/Auth/EmailSendThrottleTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using PSMPE.Portal.Application.Auth;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Auth;

public class EmailSendThrottleTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public EmailSendThrottleTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static string UniqueIp() => $"198.51.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 255)}";

    private async Task<HttpResponseMessage> SendAsync(string path, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-Forwarded-For", UniqueIp());
        return await _client.SendAsync(request);
    }

    private async Task<string> VerifiedAccountAsync()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var register = await SendAsync("/api/auth/register",
            new RegisterRequest(email, "Password123!", "Throttle Tester", DataPrivacyConsent: true));
        var body = await register.Content.ReadFromJsonAsync<RegisterResponse>();

        var query = QueryHelpers.ParseQuery(new Uri(body!.DevVerificationLink!).Query);
        await SendAsync("/api/auth/verify-email",
            new VerifyEmailRequest(Guid.Parse(query["userId"]!), query["token"]!));

        return email;
    }

    private async Task<ForgotPasswordResponse> ForgotPasswordAsync(string email)
    {
        var response = await SendAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ForgotPasswordResponse>())!;
    }

    [Fact]
    public async Task ForgotPassword_FourthSendForOneAddress_IsSuppressed()
    {
        var email = await VerifiedAccountAsync();

        for (var i = 0; i < 3; i++)
        {
            Assert.NotNull((await ForgotPasswordAsync(email)).DevResetLink);
        }

        Assert.Null((await ForgotPasswordAsync(email)).DevResetLink);
    }

    [Fact]
    public async Task ForgotPassword_ThrottlingIsPerAddressNotGlobal()
    {
        var exhausted = await VerifiedAccountAsync();
        for (var i = 0; i < 4; i++)
        {
            await ForgotPasswordAsync(exhausted);
        }

        var other = await VerifiedAccountAsync();
        Assert.NotNull((await ForgotPasswordAsync(other)).DevResetLink);
    }

    [Fact]
    public async Task ForgotPassword_ThrottledResponse_IsIndistinguishableFromAnUnthrottledOne()
    {
        var email = await VerifiedAccountAsync();
        var first = await ForgotPasswordAsync(email);
        for (var i = 0; i < 3; i++)
        {
            await ForgotPasswordAsync(email);
        }

        var throttled = await ForgotPasswordAsync(email);

        // Same message and status as a served request - the throttle must not become the
        // enumeration oracle the endpoint otherwise avoids being.
        Assert.Equal(first.Message, throttled.Message);
    }
}
```

- [ ] **4.2 Run the tests to verify they fail**

Run: `dotnet test src/PSMPE.Portal.sln --filter EmailSendThrottleTests`
Expected: FAIL — `ForgotPassword_FourthSendForOneAddress_IsSuppressed` fails because the 4th link is still non-null. The other two pass already.

- [ ] **4.3 Add the throttle interface**

Create `src/PSMPE.Portal.Application/Common/Interfaces/IEmailSendThrottle.cs`:

```csharp
namespace PSMPE.Portal.Application.Common.Interfaces;

/// <summary>
/// Caps outbound account emails per address. Partitioned on the email address rather than the
/// client IP, so it lives here instead of in the rate limiter middleware - a limiter partition
/// function can't read the request body without buffering it for every request.
/// </summary>
public interface IEmailSendThrottle
{
    /// <summary>
    /// Records a send against <paramref name="emailAddress"/> and returns false if the address
    /// has already used its allowance for the current window.
    /// </summary>
    bool TryRecordSend(string emailAddress);
}
```

- [ ] **4.4 Implement it**

Create `src/PSMPE.Portal.Infrastructure/Services/MemoryCacheEmailSendThrottle.cs`:

```csharp
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.Infrastructure.Services;

/// <summary>
/// Fixed window per email address, backed by the process-wide IMemoryCache already registered
/// for caching (see DependencyInjection). Storing the window end alongside the count keeps the
/// window fixed - re-setting the entry with a fresh expiry on every send would silently turn it
/// into a sliding window and let a steady drip of requests never reset.
/// </summary>
public class MemoryCacheEmailSendThrottle(IMemoryCache cache, IConfiguration configuration) : IEmailSendThrottle
{
    private static readonly object Gate = new();

    public bool TryRecordSend(string emailAddress)
    {
        var permitLimit = configuration.GetValue<int?>("RateLimit:EmailSendPerAddress:PermitLimit") ?? 3;
        var windowMinutes = configuration.GetValue<int?>("RateLimit:EmailSendPerAddress:WindowMinutes") ?? 60;
        var key = $"email-send-throttle:{emailAddress.Trim().ToLowerInvariant()}";
        var now = DateTimeOffset.UtcNow;

        lock (Gate)
        {
            if (!cache.TryGetValue<(int Count, DateTimeOffset WindowEnd)>(key, out var entry)
                || entry.WindowEnd <= now)
            {
                entry = (0, now.AddMinutes(windowMinutes));
            }

            if (entry.Count >= permitLimit)
            {
                return false;
            }

            cache.Set(key, (entry.Count + 1, entry.WindowEnd), entry.WindowEnd);
            return true;
        }
    }
}
```

- [ ] **4.5 Register it**

In `src/PSMPE.Portal.Infrastructure/DependencyInjection.cs`, directly after `services.AddSingleton<ICacheService, MemoryCacheService>();`:

```csharp
        services.AddSingleton<IEmailSendThrottle, MemoryCacheEmailSendThrottle>();
```

- [ ] **4.6 Apply it in the controller**

In `src/PSMPE.Portal.WebAPI/Controllers/AuthController.cs`, add `IEmailSendThrottle emailSendThrottle,` to the primary constructor parameter list, directly after `IEmailSender emailSender,`.

In `ForgotPassword`, insert directly above `var token = await userManager.GeneratePasswordResetTokenAsync(user);`:

```csharp
        if (!emailSendThrottle.TryRecordSend(request.Email))
        {
            // Same generic response as every other path here - a throttled caller must not be
            // able to tell they were throttled, let alone that the account exists.
            return Ok(new ForgotPasswordResponse(genericMessage));
        }
```

In `ResendVerificationEmail`, insert directly above `var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);`:

```csharp
        if (!emailSendThrottle.TryRecordSend(request.Email))
        {
            return Ok(new ResendVerificationEmailResponse(genericMessage));
        }
```

- [ ] **4.7 Run the tests to verify they pass**

Run: `dotnet test src/PSMPE.Portal.sln --filter EmailSendThrottleTests`
Expected: PASS — 3 passed.

- [ ] **4.8 Commit**

```bash
git add src/PSMPE.Portal.Application/Common/Interfaces/IEmailSendThrottle.cs src/PSMPE.Portal.Infrastructure/Services/MemoryCacheEmailSendThrottle.cs src/PSMPE.Portal.Infrastructure/DependencyInjection.cs src/PSMPE.Portal.WebAPI/Controllers/AuthController.cs tests/PSMPE.Portal.WebAPI.IntegrationTests/Auth/EmailSendThrottleTests.cs
git commit -m "Throttle password reset and verification emails per address"
```

---

## 5. Client IP diagnostics endpoint

This makes Task 1's tests pass and gives a one-curl post-deploy check.

**Files:**
- Modify: `src/PSMPE.Portal.WebAPI/Controllers/AdminController.cs`

- [ ] **5.1 Add the endpoint**

In `src/PSMPE.Portal.WebAPI/Controllers/AdminController.cs`, add as the first action in the class body:

```csharp
    /// <summary>
    /// Confirms the nginx -> ForwardedHeaders trust chain end to end after a deploy. If this
    /// returns a 172.x address, X-Forwarded-For isn't reaching the app and every rate limit
    /// partition has collapsed into one bucket.
    /// </summary>
    [HttpGet("diagnostics/client-ip")]
    [Authorize(Policy = PolicyNames.RequireAdmin)]
    public ActionResult<object> GetClientIp() =>
        Ok(new { clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() });
```

- [ ] **5.2 Run Task 1's tests to verify they now pass**

Run: `dotnet test src/PSMPE.Portal.sln --filter ForwardedHeadersTests`
Expected: PASS — 4 passed.

- [ ] **5.3 Run the full suite**

Run: `dotnet test src/PSMPE.Portal.sln`
Expected: PASS.

- [ ] **5.4 Commit**

```bash
git add src/PSMPE.Portal.WebAPI/Controllers/AdminController.cs
git commit -m "Add admin client-IP diagnostics endpoint"
```

---

## 6. Frontend 429 and lockout handling

**Files:**
- Modify: `apps/web/src/core/api/apiClient.ts:26-37`
- Modify: `apps/web/src/integrations/template/pages/LoginPage.tsx:49-57`

- [ ] **6.0 Add the `ACCOUNT_LOCKED` branch to the login page**

Task 3 made `login` return **403 with `code = "ACCOUNT_LOCKED"`**. `LoginPage.tsx` currently branches on `403 + EMAIL_NOT_CONFIRMED` (line 49) and `401` (line 52), so a locked-out member falls through to the generic arm and is told *"Something went wrong on our end. Please try again in a moment."* — which is both wrong and unactionable. They are locked out, it is not a server fault, and retrying immediately is exactly what won't work.

Add a branch alongside the existing `EMAIL_NOT_CONFIRMED` check, matching its shape:

```tsx
        if (err.response?.status === 403 && (err.response.data as { code?: string } | undefined)?.code === 'ACCOUNT_LOCKED') {
          setError('This account is temporarily locked after too many failed sign-in attempts. Please try again later.')
        } else if (err.response?.status === 403 && (err.response.data as { code?: string } | undefined)?.code === 'EMAIL_NOT_CONFIRMED') {
```

Keep the message consistent with the server's `lockedMessage` in `AuthController.Login`.

- [ ] **6.1 Add the 429 branch**

In `apps/web/src/core/api/apiClient.ts`, replace the response interceptor with:

```ts
export type RateLimitListener = (retryAfterSeconds: number) => void

let rateLimitListener: RateLimitListener | null = null

/** Lets the UI surface a wait time without this module importing anything from the UI layer. */
export const onRateLimited = (listener: RateLimitListener | null) => {
  rateLimitListener = listener
}

// TODO: implement refresh-token rotation once the backend issues refresh tokens;
// for now a 401 simply clears the session and sends the user back to /login.
apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      tokenStorage.clear()
      if (window.location.pathname !== '/login') {
        window.location.assign('/login')
      }
    }
    // Deliberately NOT the 401 path: being throttled is not being logged out, so the session
    // must survive and the user must stay on the page they're on.
    if (error.response?.status === 429) {
      const header = error.response.headers?.['retry-after']
      const retryAfterSeconds = Number.parseInt(header ?? '', 10)
      rateLimitListener?.(Number.isFinite(retryAfterSeconds) ? retryAfterSeconds : 60)
    }
    return Promise.reject(error)
  },
)
```

- [ ] **6.2 Verify lint and build pass**

Run: `cd apps/web && npm run lint && npm run build`
Expected: PASS, no new warnings.

- [ ] **6.3 Verify the behaviour manually**

`apps/web` has **no test runner configured** (no vitest, jest, or testing-library in `package.json`), so the spec's "throttled request keeps the user signed in" scenario cannot be asserted automatically without adding a whole test stack — out of scope for this change. Verify by hand instead, against staging after Task 9:

1. Sign in, then open devtools → Network.
2. Replay a request until it returns 429 (the `username-probe` limit is easiest: type rapidly in the registration username field).
3. Confirm you are **not** redirected to `/login`, and that `localStorage` still holds `psmpe.auth.token`.

If the session is cleared, the 429 branch has fallen through to the 401 path — check that the `if (error.response?.status === 429)` block is separate from the 401 block, not chained to it with `else if` after a `return`.

- [ ] **6.4 Commit**

```bash
git add apps/web/src/core/api/apiClient.ts
git commit -m "Surface 429 responses without clearing the session"
```

---

## 7. Configuration surface

**Files:**
- Modify: `docker-compose.yml`
- Modify: `.env.example`

- [ ] **7.1 Add the env vars to compose**

In `docker-compose.yml`, in the `backend.environment` block after the `Cache__*` entries:

```yaml
      RateLimit__Enabled: ${RateLimit__Enabled:-true}
      RateLimit__AuthIp__PermitLimit: ${RateLimit__AuthIp__PermitLimit:-20}
      RateLimit__AuthIp__WindowMinutes: ${RateLimit__AuthIp__WindowMinutes:-5}
      RateLimit__AuthEmailSend__PermitLimit: ${RateLimit__AuthEmailSend__PermitLimit:-10}
      RateLimit__AuthEmailSend__WindowMinutes: ${RateLimit__AuthEmailSend__WindowMinutes:-60}
      RateLimit__UsernameProbe__PermitLimit: ${RateLimit__UsernameProbe__PermitLimit:-30}
      RateLimit__UsernameProbe__WindowMinutes: ${RateLimit__UsernameProbe__WindowMinutes:-1}
      RateLimit__Global__PermitLimit: ${RateLimit__Global__PermitLimit:-300}
      RateLimit__Global__WindowMinutes: ${RateLimit__Global__WindowMinutes:-1}
      RateLimit__EmailSendPerAddress__PermitLimit: ${RateLimit__EmailSendPerAddress__PermitLimit:-3}
      RateLimit__EmailSendPerAddress__WindowMinutes: ${RateLimit__EmailSendPerAddress__WindowMinutes:-60}
      Lockout__MaxFailedAttempts: ${Lockout__MaxFailedAttempts:-5}
      Lockout__MinutesLockedOut: ${Lockout__MinutesLockedOut:-15}
      ForwardedHeaders__KnownNetworks: ${ForwardedHeaders__KnownNetworks:-172.16.0.0/12}
```

- [ ] **7.2 Document them in `.env.example`**

Append to `.env.example`:

```bash
# Rate limiting (see openspec/changes/add-auth-rate-limiting/). RateLimit__Enabled=false is a
# global kill switch. Limits are per client IP except EmailSendPerAddress, which is per email.
RateLimit__Enabled=true
RateLimit__AuthIp__PermitLimit=20
RateLimit__AuthIp__WindowMinutes=5
RateLimit__EmailSendPerAddress__PermitLimit=3
RateLimit__EmailSendPerAddress__WindowMinutes=60
Lockout__MaxFailedAttempts=5
Lockout__MinutesLockedOut=15
# CIDRs whose X-Forwarded-For is trusted. Must cover the Docker bridge, or client IPs resolve
# to the gateway and every caller shares one rate limit bucket.
ForwardedHeaders__KnownNetworks=172.16.0.0/12
```

- [ ] **7.3 Commit**

```bash
git add docker-compose.yml .env.example
git commit -m "Expose rate limit and lockout settings as env vars"
```

---

## 8. nginx: forward the client IP (droplet, manual)

Not in git — `/etc/nginx` lives only on the droplet. Do this **before** deploying, so the app never runs with limiting on and no client IP.

- [ ] **8.1 Back up the current config**

```bash
ssh 139.59.224.32 'cp /etc/nginx/sites-available/psmpe.org /etc/nginx/sites-available/psmpe.org.bak-$(date +%F)'
```

- [ ] **8.2 Add the headers to both `/api/` blocks**

Edit `/etc/nginx/sites-available/psmpe.org`. In the `location /api/` block of **both** the `staging.psmpe.org` server (proxying to `:5001`) and the `portal.psmpe.org` server (proxying to `:5000`), add beneath the existing `proxy_set_header Host $host;`:

```nginx
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
```

Do **not** replace these with `include proxy_params;`. That file sets `Host $http_host` while the vhost sets `Host $host`; they differ when the client sends a port, and this change must not alter host resolution.

- [ ] **8.3 Validate and reload**

```bash
ssh 139.59.224.32 'nginx -t && systemctl reload nginx'
```

Expected: `syntax is ok`, `test is successful`, then a silent reload.

---

## 9. Deploy to staging and verify the chain

- [ ] **9.1 Push and let staging deploy**

```bash
git push origin develop
```

Then merge `develop` into `staging` and push, per `BRANCHING.md`. Wait for `deploy-staging.yml` to finish.

- [ ] **9.2 Confirm the resolved client IP is real**

```bash
TOKEN=$(curl -s -X POST https://staging.psmpe.org/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"<admin email>","password":"<admin password>"}' | jq -r .token)

curl -s https://staging.psmpe.org/api/admin/diagnostics/client-ip -H "Authorization: Bearer $TOKEN"
```

Expected: your own public IP. **If it returns a `172.x.x.x` address, stop** — nginx is not forwarding, Task 8 did not take effect, and every caller is sharing one bucket. Re-check Task 8.2 was applied to the *staging* server block too.

- [ ] **9.3 Confirm limiting actually triggers**

```bash
for i in $(seq 1 25); do
  curl -s -o /dev/null -w "%{http_code}\n" -X POST https://staging.psmpe.org/api/auth/login \
    -H 'Content-Type: application/json' \
    -d '{"email":"nobody@example.com","password":"wrong"}'
done
```

Expected: roughly 20 × `401`, then `429` for the rest.

- [ ] **9.4 Confirm no warning was logged**

```bash
ssh 139.59.224.32 'docker logs psmpe-staging-backend-1 2>&1 | grep -i "known-proxy network" | head'
```

Expected: no output. Any match means the trust chain is broken despite 9.2.

---

## 10. Close the nginx bypass

Separate step with its own verification: this is the only change that can take the site offline.

**Files:**
- Modify: `docker-compose.yml`

- [ ] **10.1 Confirm the bypass exists first**

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://139.59.224.32:5000/health
```

Expected: `200` — nginx is bypassable today. This is the behaviour being removed.

- [ ] **10.2 Bind the published ports to loopback**

In `docker-compose.yml`, change the `backend` and `frontend` port mappings:

```yaml
    ports:
      - "127.0.0.1:${BACKEND_PORT:-5000}:8080"
```

```yaml
    ports:
      - "127.0.0.1:${FRONTEND_PORT:-5173}:80"
```

nginx proxies to `localhost`, so this is transparent to it. Leave `postgres` alone — its exposure is a separate problem noted in the proposal's Out of Scope.

> Staging layers a droplet-local `docker-compose.ports.yml` for port overrides (see README). Check it does not re-publish on `0.0.0.0`, or this change is undone there: `ssh 139.59.224.32 'cat /opt/psmpe-portal/staging/docker-compose.ports.yml'`

- [ ] **10.3 Commit and deploy to staging**

```bash
git add docker-compose.yml
git commit -m "Bind published container ports to loopback"
git push origin develop
```

Merge to `staging`, push, wait for the deploy.

- [ ] **10.4 Verify the site still works and the bypass is gone**

```bash
curl -s -o /dev/null -w "site: %{http_code}\n" https://staging.psmpe.org/
curl -s -o /dev/null -w "api:  %{http_code}\n" https://staging.psmpe.org/api/auth/username-available?username=probe
curl -s -m 5 -o /dev/null -w "bypass: %{http_code}\n" http://139.59.224.32:5001/health || echo "bypass: refused (expected)"
```

Expected: site `200`, api `200`, bypass refused or timed out. **If the site returns 502, roll back immediately** — nginx and the new binding disagree:

```bash
git revert --no-edit HEAD && git push origin develop
```

---

## 11. Production rollout

- [ ] **11.1 Merge to main**

Merge `develop` → `main` and push. `deploy-production.yml` runs automatically.

- [ ] **11.2 Repeat the staging verification against production**

Run steps 9.2, 9.3, and 10.4 against `portal.psmpe.org` / `139.59.224.32:5000`, using `psmpe-production-backend-1` for the log check in 9.4.

Expected: identical results. Same rollback rule — 502 means revert.

---

## 12. Update the API contract doc

**Files:**
- Modify: `openspecs/auth.md`

- [ ] **12.1 Document the new behaviour**

Add to `openspecs/auth.md`, following the file's existing per-feature structure:

- All auth endpoints are rate limited per client IP; exceeding a limit returns **429** with a `ProblemDetails` body and a `Retry-After` header.
- `login` returns **403** with `code = "ACCOUNT_LOCKED"` after 5 consecutive failed passwords, for 15 minutes, counted per account and independent of client IP.
- `forgot-password` and `resend-verification-email` send at most 3 emails per address per hour; a suppressed send is indistinguishable from a served one.
- Limits and lockout thresholds are configurable via `RateLimit__*` / `Lockout__*` env vars; `RateLimit__Enabled=false` disables limiting entirely.

- [ ] **12.2 Commit**

```bash
git add openspecs/auth.md
git commit -m "Document rate limiting and lockout in the auth contract"
```

---

## Spec scenarios verified manually, not by test

Two scenarios in `specs/auth/spec.md` have no automated coverage. Both are deliberate, and both
are covered by a manual step above — noted here so the gap is visible rather than assumed closed:

- **"Misconfigured trust chain is reported"** (warn-once logging). The flag is a process-wide
  `Interlocked` static, so a test asserting it would pass or fail depending on which test ran
  first. Verified by step 9.4 instead.
- **"Throttled request keeps the user signed in"** (frontend). No test runner exists in
  `apps/web`. Verified by step 6.3 instead.

## Verification checklist

- [ ] `dotnet test src/PSMPE.Portal.sln` passes
- [ ] `cd apps/web && npm run lint && npm run build` passes
- [ ] `/api/admin/diagnostics/client-ip` returns a real public IP on staging and production
- [ ] No "known-proxy network" warning in either backend's logs
- [ ] 21st login attempt from one IP returns 429 with `Retry-After`
- [ ] 6th bad password on one account returns 403 `ACCOUNT_LOCKED`, and still does from a different IP
- [ ] 4th `forgot-password` for one address sends no email and looks identical to the 1st
- [ ] `http://139.59.224.32:5000/health` no longer answers; both sites still serve over HTTPS
