using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.Infrastructure.UnitTests.TestSupport;

/// <summary>
/// A clock the test moves by hand. Exists so window-boundary behaviour can be asserted in
/// milliseconds instead of waiting out a real 60-minute window.
/// </summary>
public class FakeDateTimeProvider(DateTimeOffset now) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; private set; } = now;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
