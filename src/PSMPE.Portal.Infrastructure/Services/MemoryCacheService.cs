using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.Infrastructure.Services;

/// <summary>
/// Real IMemoryCache-backed ICacheService implementation - registered as a singleton (see
/// DependencyInjection.AddInfrastructure), matching IMemoryCache's own process-wide lifetime.
/// "Cache:Enabled" (default true) is a global kill switch - set false to reproduce today's
/// uncached behavior without a redeploy, e.g. while ruling out a caching bug in production.
/// </summary>
public class MemoryCacheService(IMemoryCache cache, IConfiguration configuration) : ICacheService
{
    private readonly bool enabled = configuration.GetValue<bool?>("Cache:Enabled") ?? true;

    public async Task<T> GetOrCreateAsync<T>(string key, string durationConfigKey, int defaultDurationSeconds, Func<Task<T>> factory)
    {
        if (!enabled)
        {
            return await factory();
        }

        if (cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        var value = await factory();
        var durationSeconds = configuration.GetValue<int?>(durationConfigKey) ?? defaultDurationSeconds;
        cache.Set(key, value, TimeSpan.FromSeconds(durationSeconds));
        return value;
    }

    public void Remove(string key) => cache.Remove(key);
}
