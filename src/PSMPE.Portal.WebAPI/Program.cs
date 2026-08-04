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
    var cidrs = builder.Configuration["ForwardedHeaders:KnownNetworks"] ?? "172.16.0.0/12";
    foreach (var cidr in cidrs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var parts = cidr.Split('/');
        // Fully qualified: System.Net.IPNetwork (.NET 8) is a different type with the same name,
        // and KnownNetworks takes the HttpOverrides one.
        options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(
            IPAddress.Parse(parts[0]), int.Parse(parts[1])));
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
