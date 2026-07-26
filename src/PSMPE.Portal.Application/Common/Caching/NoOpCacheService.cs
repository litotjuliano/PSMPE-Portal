using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.Application.Common.Caching;

/// <summary>
/// Default fallback for services whose caching dependency is an optional constructor parameter
/// (e.g. `ContentService(IApplicationDbContext db, ICurrentUserService currentUser, ICacheService?
/// cache = null)`) - always calls straight through to the factory, never actually caches. This is
/// what existing unit tests get when they construct a service directly without supplying a cache
/// (e.g. `new MemberService(db)`), so those 50+ call sites keep compiling and behaving exactly as
/// before; only the real DI-resolved MemoryCacheService in production actually caches.
/// </summary>
public sealed class NoOpCacheService : ICacheService
{
    public static readonly NoOpCacheService Instance = new();

    private NoOpCacheService()
    {
    }

    public Task<T> GetOrCreateAsync<T>(string key, string durationConfigKey, int defaultDurationSeconds, Func<Task<T>> factory) => factory();

    public void Remove(string key)
    {
    }
}
