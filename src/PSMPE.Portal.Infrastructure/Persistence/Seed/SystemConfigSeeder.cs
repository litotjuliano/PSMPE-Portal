using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Application.Common.Configuration;
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
        ("MembershipGracePeriodDays", "30", "Days after RenewalDueDate a member keeps limited portal access before being treated as fully Expired."),
        .. MembershipFeeKeys.All.Select(f => (f.Key, f.Default.ToString(CultureInfo.InvariantCulture), f.Description)),
    ];

    public static async Task SeedAsync(ApplicationDbContext db, ILogger logger)
    {
        // Per-key rather than the original all-or-nothing `if (!AnyAsync())`: that only ever seeded
        // a completely empty table, so any key added after the first deployment would never appear
        // on an existing database. Each key is now filled in independently, and an admin-edited
        // value is left alone.
        var existingKeys = await db.SystemConfigs.Select(c => c.Key).ToListAsync();
        var missing = DefaultConfig.Where(c => !existingKeys.Contains(c.Key)).ToArray();

        foreach (var (key, value, description) in missing)
        {
            db.SystemConfigs.Add(new SystemConfig { Key = key, Value = value, Description = description });
        }

        if (missing.Length > 0)
        {
            logger.LogInformation("Seeded {Count} missing system configuration rows.", missing.Length);
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
