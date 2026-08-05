using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.Infrastructure.Services;

/// <summary>
/// Fixed window per email address, backed by the process-wide IMemoryCache already registered
/// for caching (see DependencyInjection). Storing the window end alongside the count keeps the
/// window fixed - re-setting the entry with a fresh expiry on every send would silently turn it
/// into a sliding window and let a steady drip of requests never reset.
///
/// Expiry is enforced twice over, and deliberately so. The explicit WindowEnd comparison is the
/// authority; the cache's absolute expiration only reclaims the memory. They agree in a real host
/// because both run off the wall clock, but the cache always uses real time while the comparison
/// uses IDateTimeProvider - which is what lets a test advance a fake clock across a window
/// boundary without waiting an hour. Assert on TryRecordSend's result, never on cache contents.
/// </summary>
public class MemoryCacheEmailSendThrottle(
    IMemoryCache cache,
    IConfiguration configuration,
    IDateTimeProvider dateTimeProvider) : IEmailSendThrottle
{
    private static readonly object Gate = new();

    public bool TryRecordSend(string emailAddress)
    {
        var permitLimit = configuration.GetValue<int?>("RateLimit:EmailSendPerAddress:PermitLimit") ?? 3;
        var windowMinutes = configuration.GetValue<int?>("RateLimit:EmailSendPerAddress:WindowMinutes") ?? 60;
        var key = $"email-send-throttle:{emailAddress.Trim().ToLowerInvariant()}";
        var now = dateTimeProvider.UtcNow;

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
