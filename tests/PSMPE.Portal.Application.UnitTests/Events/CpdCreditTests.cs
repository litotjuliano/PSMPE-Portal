using PSMPE.Portal.Application.Events;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Events;

public class CpdCreditTests
{
    private static EventRegistration Registration(EventMode mode, EventRegistrationStatus status = EventRegistrationStatus.EvaluationSubmitted) =>
        new() { Mode = mode, Status = status };

    [Fact]
    public void For_NotEvaluationSubmitted_ReturnsNull()
    {
        var registration = Registration(EventMode.Onsite, EventRegistrationStatus.Attended);
        var @event = new Event { CpdUnitsOnsite = 8m };

        var credit = CpdCredit.For(registration, @event, sessionsAttended: 6, totalSessions: 6);

        Assert.Null(credit);
    }

    [Fact]
    public void For_ApplicableModalityUnitsStillNull_ReturnsNull()
    {
        var registration = Registration(EventMode.Online);
        var @event = new Event { CpdUnitsOnsite = 8m, CpdUnitsOnline = null };

        var credit = CpdCredit.For(registration, @event, sessionsAttended: 6, totalSessions: 6);

        Assert.Null(credit);
    }

    /// <summary>Matches spec.md's "Partial attendance earns prorated credit": 3 of 6 sessions on an
    /// 8-unit event earns 4 (8 x 3/6).</summary>
    [Fact]
    public void For_PartialAttendance_ReturnsProratedValue()
    {
        var registration = Registration(EventMode.Onsite);
        var @event = new Event { CpdUnitsOnsite = 8m };

        var credit = CpdCredit.For(registration, @event, sessionsAttended: 3, totalSessions: 6);

        Assert.Equal(4m, credit);
    }

    /// <summary>Matches spec.md's "Onsite and Online registrations on the same event earn different
    /// credit".</summary>
    [Theory]
    [InlineData(EventMode.Onsite, 8)]
    [InlineData(EventMode.Online, 4)]
    public void For_FullAttendance_UsesUnitsForTheRegistrationsOwnMode(EventMode mode, decimal expected)
    {
        var registration = Registration(mode);
        var @event = new Event { CpdUnitsOnsite = 8m, CpdUnitsOnline = 4m };

        var credit = CpdCredit.For(registration, @event, sessionsAttended: 6, totalSessions: 6);

        Assert.Equal(expected, credit);
    }

    /// <summary>The raw division for a non-evenly-divisible fraction (8 * 1 / 3) produces up to 28
    /// decimal digits; CpdCredit.For rounds to match CpdUnitsOnsite/CpdUnitsOnline's own
    /// HasPrecision(6, 2) in EventConfiguration.cs.</summary>
    [Fact]
    public void For_NonEvenlyDivisibleAttendance_RoundsToTwoDecimalPlaces()
    {
        var registration = Registration(EventMode.Onsite);
        var @event = new Event { CpdUnitsOnsite = 8m };

        var credit = CpdCredit.For(registration, @event, sessionsAttended: 1, totalSessions: 3);

        Assert.Equal(2.67m, credit);
    }

    [Fact]
    public void For_ZeroTotalSessions_ReturnsNull()
    {
        var registration = Registration(EventMode.Onsite);
        var @event = new Event { CpdUnitsOnsite = 8m };

        var credit = CpdCredit.For(registration, @event, sessionsAttended: 0, totalSessions: 0);

        Assert.Null(credit);
    }
}
