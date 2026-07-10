using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.Infrastructure.Persistence.Seed;

namespace PSMPE.Portal.WebAPI.IntegrationTests;

/// <summary>
/// Boots the real WebAPI host but swaps the Npgsql-backed DbContext for an EF Core InMemory
/// database, so auth/authorization tests don't require a running PostgreSQL instance.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"psmpe-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "integration-test-signing-key-at-least-32-chars-long",
                ["Jwt:Issuer"] = "PSMPE.Portal.Tests",
                ["Jwt:Audience"] = "PSMPE.Portal.Tests.Client",
                ["Seed:Enabled"] = "false",
                ["OpenAI:ApiKey"] = "test-key-not-used"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }

    /// <summary>
    /// Ensures the InMemory database exists and the fixed role set (with its default permission
    /// grants) is seeded, ready for auth tests. Reuses the real IdentitySeeder rather than
    /// hand-rolling role creation, so integration tests reflect actual seed behavior.
    /// </summary>
    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<CustomWebApplicationFactory>>();

        await IdentitySeeder.SeedAsync(roleManager, userManager, configuration, logger);
    }
}
