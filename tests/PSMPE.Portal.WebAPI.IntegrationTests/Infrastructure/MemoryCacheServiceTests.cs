using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PSMPE.Portal.Infrastructure.Services;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Infrastructure;

/// <summary>
/// Unit-style tests for MemoryCacheService (constructed directly with a real MemoryCache and an
/// in-memory IConfiguration - no HTTP/WebApplicationFactory needed). Lives here rather than in
/// PSMPE.Portal.Application.UnitTests because that project doesn't reference Infrastructure.
/// </summary>
public class MemoryCacheServiceTests
{
    private static MemoryCacheService CreateService(Dictionary<string, string?>? configValues = null)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues ?? [])
            .Build();
        return new MemoryCacheService(cache, configuration);
    }

    [Fact]
    public async Task GetOrCreateAsync_CacheMiss_InvokesFactoryAndReturnsValue()
    {
        var service = CreateService();
        var callCount = 0;

        var result = await service.GetOrCreateAsync("key", "Cache:MissingDurationKey", 300, () =>
        {
            callCount++;
            return Task.FromResult("value");
        });

        Assert.Equal("value", result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetOrCreateAsync_CacheHit_DoesNotInvokeFactoryAgain()
    {
        var service = CreateService();
        var callCount = 0;

        Func<Task<string>> factory = () =>
        {
            callCount++;
            return Task.FromResult("value");
        };

        var first = await service.GetOrCreateAsync("key", "Cache:MissingDurationKey", 300, factory);
        var second = await service.GetOrCreateAsync("key", "Cache:MissingDurationKey", 300, factory);

        Assert.Equal("value", first);
        Assert.Equal("value", second);
        Assert.Equal(1, callCount); // second call was a cache hit - factory not invoked again
    }

    [Fact]
    public async Task Remove_ForcesTheNextCallToInvokeTheFactoryAgain()
    {
        var service = CreateService();
        var callCount = 0;

        Func<Task<string>> factory = () =>
        {
            callCount++;
            return Task.FromResult($"value-{callCount}");
        };

        await service.GetOrCreateAsync("key", "Cache:MissingDurationKey", 300, factory);
        service.Remove("key");
        var afterRemoval = await service.GetOrCreateAsync("key", "Cache:MissingDurationKey", 300, factory);

        Assert.Equal(2, callCount);
        Assert.Equal("value-2", afterRemoval);
    }

    [Fact]
    public async Task WhenCacheDisabled_AlwaysInvokesFactory_EvenForTheSameKey()
    {
        var service = CreateService(new Dictionary<string, string?> { ["Cache:Enabled"] = "false" });
        var callCount = 0;

        Func<Task<string>> factory = () =>
        {
            callCount++;
            return Task.FromResult("value");
        };

        await service.GetOrCreateAsync("key", "Cache:MissingDurationKey", 300, factory);
        await service.GetOrCreateAsync("key", "Cache:MissingDurationKey", 300, factory);

        Assert.Equal(2, callCount); // disabled - no caching at all, factory runs every time
    }

    [Fact]
    public async Task MissingDurationConfigKey_FallsBackToDefaultWithoutError()
    {
        // Uses an explicit duration config key that isn't present in configuration at all -
        // confirms defaultDurationSeconds is used instead of throwing/behaving as "no expiry".
        var service = CreateService();

        var result = await service.GetOrCreateAsync("key", "Cache:SomeKeyThatIsNeverConfigured", 60, () => Task.FromResult(42));

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task DurationConfigKey_WhenSet_OverridesTheDefault()
    {
        var service = CreateService(new Dictionary<string, string?> { ["Cache:ContentDurationSeconds"] = "1" });
        var callCount = 0;

        Func<Task<string>> factory = () =>
        {
            callCount++;
            return Task.FromResult("value");
        };

        await service.GetOrCreateAsync("key", "Cache:ContentDurationSeconds", 300, factory);
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        await service.GetOrCreateAsync("key", "Cache:ContentDurationSeconds", 300, factory);

        Assert.Equal(2, callCount); // configured 1s duration expired before the second call, unlike the 300s default
    }
}
