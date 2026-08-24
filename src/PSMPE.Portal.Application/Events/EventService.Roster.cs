using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Events.Dtos;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Events;

public partial class EventService
{
    public async Task<Result<EventRosterDto>> GetRosterAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var @event = await db.Events.Include(e => e.Sessions).FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
        if (@event is null)
        {
            return Result<EventRosterDto>.NotFound($"Event '{eventId}' was not found.");
        }

        var registrations = await db.EventRegistrations.Include(r => r.Member)
            .Where(r => r.EventId == eventId && r.Status != EventRegistrationStatus.Cancelled)
            .ToListAsync(cancellationToken);
        var registrationIds = registrations.Select(r => r.Id).ToList();

        var attendanceByRegistration = (await db.EventAttendances
            .Where(a => registrationIds.Contains(a.EventRegistrationId))
            .ToListAsync(cancellationToken))
            .GroupBy(a => a.EventRegistrationId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(a => a.EventSessionId).ToList());

        // "Most recent payment wins" - a registration can have more than one Payment row over time
        // (e.g. a Rejected one followed by a resubmission), and the roster should show the latest
        // one, not an arbitrary one. See Domain/Entities/Payment.cs's doc comment.
        var paymentByRegistration = (await db.Payments
            .Where(p => p.EventRegistrationId != null && registrationIds.Contains(p.EventRegistrationId!.Value))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken))
            .GroupBy(p => p.EventRegistrationId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var totalSessions = @event.Sessions.Count;
        var entries = registrations.Select(r =>
        {
            var attendedSessionIds = attendanceByRegistration.GetValueOrDefault(r.Id, []);
            paymentByRegistration.TryGetValue(r.Id, out var payment);
            return new EventRosterEntryDto(
                r.Id, r.MemberId, $"{r.Member.FirstName} {r.Member.LastName}", r.Member.MembershipNo,
                r.Mode.ToString(), r.Status.ToString(), attendedSessionIds, totalSessions,
                payment?.Id, payment?.Status.ToString(), payment is null ? null : payment.ProofStorageKey is null,
                payment?.RejectedReason, r.EvaluationRating, r.EvaluationSubmittedAt,
                CpdCredit.For(r, @event, attendedSessionIds.Count, totalSessions));
        }).ToList();

        var sessions = @event.Sessions.OrderBy(s => s.Order)
            .Select(s => new EventSessionDto(s.Id, s.Title, s.StartsAt, s.EndsAt, s.Order))
            .ToList();

        return Result<EventRosterDto>.Success(new EventRosterDto(@event.Id, @event.Title, sessions, entries));
    }
}
