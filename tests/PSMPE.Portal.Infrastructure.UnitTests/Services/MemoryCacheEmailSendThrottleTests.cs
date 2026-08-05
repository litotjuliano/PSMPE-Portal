using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PSMPE.Portal.Infrastructure.Services;
using PSMPE.Portal.Infrastructure.UnitTests.TestSupport;

namespace PSMPE.Portal.Infrastructure.UnitTests.Services;

/// <summary>
/// The window mechanics the integration tests cannot reach: they drive real HTTP against a real
/// 60-minute window, so nothing there can distinguish a fixed window from a sliding one.
/// </summary>
public class MemoryCacheEmailSendThrottleTests
{
    private const int PermitLimit = 3;
    private const int WindowMinutes = 60;

    /// <summary>
    /// The fake clock starts at the real current time on purpose. IMemoryCache evicts on absolute
    /// expiration using the real wall clock regardless of this provider, so a clock based at, say,
    /// 2020 would write entries that are already expired and every send would see a fresh counter.
    /// Starting at "now" keeps every expiration this test writes in the real future, leaving the
    /// throttle's own WindowEnd comparison - the thing under test - as the only expiry that fires.
    /// </summary>
    private static (MemoryCacheEmailSendThrottle Throttle, FakeDateTimeProvider Clock) Build(
        int permitLimit = PermitLimit,
        int windowMinutes = WindowMinutes)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimit:EmailSendPerAddress:PermitLimit"] = permitLimit.ToString(),
                ["RateLimit:EmailSendPerAddress:WindowMinutes"] = windowMinutes.ToString(),
            })
            .Build();

        var clock = new FakeDateTimeProvider(DateTimeOffset.UtcNow);
        var cache = new MemoryCache(new MemoryCacheOptions());
        return (new MemoryCacheEmailSendThrottle(cache, configuration, clock), clock);
    }

    [Fact]
    public void Allowance_IsSpentWithinTheWindow_AndRestoredOnlyAfterItEnds()
    {
        var (throttle, clock) = Build();
        const string address = "member@example.com";

        for (var i = 0; i < PermitLimit; i++)
        {
            Assert.True(throttle.TryRecordSend(address), $"send {i + 1} should be permitted");
        }

        Assert.False(throttle.TryRecordSend(address));

        // Still inside the window: the count must survive the passage of time, not decay with it.
        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.False(throttle.TryRecordSend(address));

        // Past the window end: the counter resets wholesale.
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(throttle.TryRecordSend(address));
    }

    [Fact]
    public void SendsSpreadAcrossTheWindow_DoNotPushItsEndForward()
    {
        var (throttle, clock) = Build();
        const string address = "member@example.com";

        // A drip rather than a burst. This is what separates a fixed window from a sliding one:
        // if each send reset the expiry, the window would end at minute 119 instead of 60 and the
        // address would never regain its allowance under steady traffic.
        Assert.True(throttle.TryRecordSend(address));
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.True(throttle.TryRecordSend(address));
        clock.Advance(TimeSpan.FromMinutes(29));
        Assert.True(throttle.TryRecordSend(address));

        // Minute 59: allowance spent, window still open under either scheme.
        Assert.False(throttle.TryRecordSend(address));

        // Minute 61: past the end of the window opened by the FIRST send. A sliding window would
        // still be closed here.
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(throttle.TryRecordSend(address));
    }

    [Theory]
    [InlineData("Member@Example.com")]
    [InlineData("  member@example.com  ")]
    [InlineData("MEMBER@EXAMPLE.COM")]
    public void AllowanceIsSharedAcrossCasingAndSurroundingWhitespace(string variant)
    {
        var (throttle, _) = Build();

        for (var i = 0; i < PermitLimit; i++)
        {
            Assert.True(throttle.TryRecordSend("member@example.com"));
        }

        // Otherwise an attacker gets a fresh allowance per spelling of the same inbox.
        Assert.False(throttle.TryRecordSend(variant));
    }

    [Fact]
    public void AllowanceIsTrackedPerAddress()
    {
        var (throttle, _) = Build();

        for (var i = 0; i < PermitLimit; i++)
        {
            Assert.True(throttle.TryRecordSend("exhausted@example.com"));
        }

        Assert.False(throttle.TryRecordSend("exhausted@example.com"));
        Assert.True(throttle.TryRecordSend("other@example.com"));
    }
}
