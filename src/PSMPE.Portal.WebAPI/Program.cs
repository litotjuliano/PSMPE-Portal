using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Application;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Infrastructure;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.Infrastructure.Persistence.Seed;
using PSMPE.Portal.WebAPI.Extensions;
using PSMPE.Portal.WebAPI.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddPortalSwagger();
builder.Services.AddHealthChecks();

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
    // Guard the PARSED result, not the raw string. ForwardedHeadersMiddleware only runs its
    // known-peer check when KnownProxies.Count + KnownNetworks.Count > 0, and both were just
    // cleared - so ending up with zero entries here silently trusts X-Forwarded-For from every
    // peer on earth, with no exception and no log. That is the exact forgery this configuration
    // exists to prevent. Null, "", "   " and "," all reach that state by different routes
    // (the indexer returns "" for a present-but-empty key; TrimEntries|RemoveEmptyEntries
    // discards the rest), and checking after the split covers all of them at once.
    const string defaultKnownNetworks = "172.16.0.0/12";
    var configured = builder.Configuration["ForwardedHeaders:KnownNetworks"] ?? string.Empty;
    var entries = configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (entries.Length == 0)
    {
        entries = [defaultKnownNetworks];
    }

    foreach (var cidr in entries)
    {
        try
        {
            var parts = cidr.Split('/');
            // Fully qualified: System.Net.IPNetwork (.NET 8) is a different type with the same name,
            // and KnownNetworks takes the HttpOverrides one.
            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(
                IPAddress.Parse(parts[0]), int.Parse(parts[1])));
        }
        catch (Exception ex)
        {
            // This throws during pipeline construction, so a bad value is a startup crash. Name the
            // key and the offending token, or the operator gets a bare FormatException on loop under
            // restart: unless-stopped with nothing pointing at the env var they just edited.
            throw new InvalidOperationException(
                $"ForwardedHeaders:KnownNetworks contains an invalid entry '{cidr}'. Expected a " +
                "comma-separated list of CIDR ranges, for example '172.16.0.0/12'.", ex);
        }
    }

    // Exactly one hop. nginx's $proxy_add_x_forwarded_for APPENDS the real peer to whatever the
    // client sent, so the rightmost entry is the only trustworthy one. Raising this would let an
    // attacker pick their own rate limit partition with a forged header.
    options.ForwardLimit = 1;
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddCors(options =>
{
    // TODO: restrict to the deployed frontend origin(s) once known; kept open for local dev.
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins("http://localhost:5173", "http://localhost:3000")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// First in the pipeline: everything downstream (CORS, auth, rate limiting, logging) should
// see the real client address, not the proxy's.
app.UseForwardedHeaders();

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Unauthenticated liveness probe used by the DigitalOcean App Platform health check
// (see infra/digitalocean/app.*.yaml). Kept simple: 200 OK means the process is up.
app.MapHealthChecks("/health");

if (builder.Configuration.GetValue<bool>("Seed:Enabled"))
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Seed");

    var db = services.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    await IdentitySeeder.SeedAsync(roleManager, userManager, builder.Configuration, logger);
    await SystemConfigSeeder.SeedAsync(db, logger);
    await MemberSeeder.SeedAsync(db, userManager, builder.Configuration, logger);
}

// Unconditional (not gated by Seed:Enabled) - fixes real corrupted data (administrative accounts
// that ended up with a Member row via a since-fixed bug), so it self-heals every environment,
// not just dev/test. Idempotent - a no-op once the data is clean.
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Cleanup");
    var db = services.GetRequiredService<ApplicationDbContext>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    await SystemAccountMemberCleanup.CleanupAsync(db, userManager, logger);
}

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program;
