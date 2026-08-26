using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.Application.Members;

/// <summary>
/// Single source of truth for "how many days of grace a lapsed member gets" - shared by
/// MemberService (member-facing IsExpired/IsInGracePeriod) and MembershipLifecycleService (the
/// background job that sends reminders and auto-flips Status). Extracted to a static helper rather
/// than a new DI service so both read the exact same cache key/fallback without either needing a
/// constructor change - MemberService in particular is constructed directly
/// (`new MemberService(db)`) by ~70 existing unit tests via an optional-cache-parameter trick (see
/// docs/caching-strategy.md), which a required new dependency would break.
/// </summary>
public static class MembershipGracePeriod
{
    public const string ConfigKey = "MembershipGracePeriodDays";
    public const int DefaultDays = 7;
    private const string CacheKey = "config:membership-grace-period-days";

    /// <summary>
    /// TTL-only expiry: nothing in the app writes this key at runtime (it's seeded at startup and
    /// changed only by editing the row directly - see the AddRenewalReminderLogAndUpdateGracePeriod
    /// migration for the one-time 30 -> 7 data fix). If a grace-period editor is ever added it must
    /// evict CacheKey on write, the same way PaymentService.UpdateFeesAsync does for its own keys.
    /// </summary>
    public static Task<int> GetDaysAsync(
        IApplicationDbContext db, ICacheService cache, CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync(CacheKey, "Cache:GracePeriodDurationSeconds", 600, async () =>
        {
            var config = await db.SystemConfigs.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Key == ConfigKey, cancellationToken);
            return config is not null && int.TryParse(config.Value, out var days) ? days : DefaultDays;
        });
}
