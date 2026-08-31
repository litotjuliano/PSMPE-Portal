using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Events.Dtos;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Events;

public partial class EventService
{
    public async Task<MyCpdSummaryDto> GetMyCpdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (member is null)
        {
            return new MyCpdSummaryDto(0m, []);
        }

        var registrations = await db.EventRegistrations
            .Include(r => r.Event)
            .Where(r => r.MemberId == member.Id && r.Status != EventRegistrationStatus.Cancelled)
            .ToListAsync(cancellationToken);
        var registrationIds = registrations.Select(r => r.Id).ToList();
        var eventIds = registrations.Select(r => r.EventId).ToList();

        var attendanceCounts = await db.EventAttendances
            .Where(a => registrationIds.Contains(a.EventRegistrationId))
            .GroupBy(a => a.EventRegistrationId)
            .Select(g => new { RegistrationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.RegistrationId, g => g.Count, cancellationToken);

        var sessionCounts = await db.EventSessions
            .Where(s => eventIds.Contains(s.EventId))
            .GroupBy(s => s.EventId)
            .Select(g => new { EventId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.EventId, g => g.Count, cancellationToken);

        var items = registrations.Select(r =>
        {
            var sessionsAttended = attendanceCounts.GetValueOrDefault(r.Id);
            var totalSessions = sessionCounts.GetValueOrDefault(r.EventId);
            var credit = CpdCredit.For(r, r.Event, sessionsAttended, totalSessions);
            return new MyCpdRegistrationDto(
                r.Id, r.EventId, r.Event.Title, r.Event.StartsAt, r.Mode.ToString(), r.Status.ToString(),
                sessionsAttended, totalSessions, credit);
        }).ToList();

        var total = items.Sum(i => i.CreditUnits ?? 0m);
        return new MyCpdSummaryDto(total, items);
    }
}
