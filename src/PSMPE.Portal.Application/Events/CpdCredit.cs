namespace PSMPE.Portal.Application.Events;

using PSMPE.Portal.Domain.Entities;

/// <summary>Real implementation lands in a later task - see CpdCreditTests.cs there. This placeholder
/// only unblocks the build for the tasks in between.</summary>
internal static class CpdCredit
{
    public static decimal? For(EventRegistration registration, Event @event, int sessionsAttended, int totalSessions) => null;
}
