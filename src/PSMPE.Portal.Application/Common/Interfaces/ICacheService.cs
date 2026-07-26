namespace PSMPE.Portal.Application.Common.Interfaces;

/// <summary>
/// Abstraction over how read-heavy, rarely-changing data gets cached - deliberately isolated so
/// the real IMemoryCache-backed implementation can live in Infrastructure (same pattern as
/// IFileStorageService/IEmailSender). See Infrastructure.Services.MemoryCacheService and
/// Common.Caching.NoOpCacheService.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/> if present and not expired; otherwise
    /// invokes <paramref name="factory"/>, caches the result, and returns it. The cache duration
    /// is read from configuration at <paramref name="durationConfigKey"/> (e.g.
    /// "Cache:ContentDurationSeconds"), falling back to <paramref name="defaultDurationSeconds"/>
    /// if that key isn't set - keeps durations configurable without every caller needing its own
    /// IConfiguration dependency.
    /// </summary>
    Task<T> GetOrCreateAsync<T>(string key, string durationConfigKey, int defaultDurationSeconds, Func<Task<T>> factory);

    /// <summary>Evicts a single cache entry - called after a mutation that would make it stale.</summary>
    void Remove(string key);
}
