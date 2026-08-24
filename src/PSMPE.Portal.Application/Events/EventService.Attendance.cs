using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Events.Dtos;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Events;

public partial class EventService
{
    public async Task<Result> RecordAttendanceAsync(
        Guid eventId, IReadOnlyList<RegistrantAttendanceRequest> registrants, Guid adminUserId,
        CancellationToken cancellationToken = default)
    {
        var registrationIds = registrants.Select(r => r.RegistrationId).ToList();
        var registrations = await db.EventRegistrations
            .Where(r => registrationIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        var validSessionIds = (await db.EventSessions
            .Where(s => s.EventId == eventId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        foreach (var registrant in registrants)
        {
            if (!registrations.TryGetValue(registrant.RegistrationId, out var registration) || registration.EventId != eventId)
            {
                return Result.Failure($"Registration '{registrant.RegistrationId}' does not belong to this event.");
            }

            if (registration.Status is not (EventRegistrationStatus.PaymentVerified or EventRegistrationStatus.Attended or EventRegistrationStatus.EvaluationSubmitted))
            {
                return Result.Failure($"Registration '{registrant.RegistrationId}' needs a verified payment before attendance can be recorded.");
            }

            if (registrant.SessionIds.Any(id => !validSessionIds.Contains(id)))
            {
                return Result.Failure("One or more sessions do not belong to this event.");
            }
        }

        foreach (var registrant in registrants)
        {
            var registration = registrations[registrant.RegistrationId];

            var existing = await db.EventAttendances
                .Where(a => a.EventRegistrationId == registrant.RegistrationId)
                .ToListAsync(cancellationToken);
            db.EventAttendances.RemoveRange(existing);

            foreach (var sessionId in registrant.SessionIds.Distinct())
            {
                db.EventAttendances.Add(new EventAttendance
                {
                    EventRegistrationId = registrant.RegistrationId,
                    EventSessionId = sessionId,
                    RecordedBy = adminUserId,
                    RecordedAt = DateTimeOffset.UtcNow,
                });
            }

            if (registrant.SessionIds.Count > 0 && registration.Status == EventRegistrationStatus.PaymentVerified)
            {
                registration.Status = EventRegistrationStatus.Attended;
                registration.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
