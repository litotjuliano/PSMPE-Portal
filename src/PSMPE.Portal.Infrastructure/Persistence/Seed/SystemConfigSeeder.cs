using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Seed;

/// <summary>Idempotently seeds starter system configuration rows and a default system layout.</summary>
public static class SystemConfigSeeder
{
    private static readonly (string Key, string Value, string Description)[] DefaultConfig =
    [
        ("SiteName", "PSMPE Portal", "Display name shown in the admin UI and page titles."),
        ("AllowPublicRegistration", "false", "Whether new users can self-register via /api/auth/register."),
        ("DefaultTheme", "light", "Default UI theme for new users."),
        ("MembershipGracePeriodDays", "30", "Days after RenewalDueDate a member keeps limited portal access before being treated as fully Expired.")
    ];

    public static async Task SeedAsync(ApplicationDbContext db, ILogger logger)
    {
        if (!await db.SystemConfigs.AnyAsync())
        {
            foreach (var (key, value, description) in DefaultConfig)
            {
                db.SystemConfigs.Add(new SystemConfig { Key = key, Value = value, Description = description });
            }

            logger.LogInformation("Seeded {Count} system configuration rows.", DefaultConfig.Length);
        }

        if (!await db.Layouts.AnyAsync(l => l.IsSystemLayout))
        {
            db.Layouts.Add(new Layout
            {
                Name = "Default System Layout",
                Definition = "{\"sections\":[\"header\",\"content\",\"footer\"]}",
                IsSystemLayout = true,
                OwnerId = null
            });

            logger.LogInformation("Seeded default system layout.");
        }

        await db.SaveChangesAsync();
    }
}
