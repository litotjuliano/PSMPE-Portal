using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Events.Dtos;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Events;

public partial class EventService
{
    public async Task<Result<EventRegistrationDto>> RegisterAsync(
        Guid userId, Guid eventId, string mode, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<EventMode>(mode, ignoreCase: true, out var parsedMode))
        {
            return Result<EventRegistrationDto>.Failure($"'{mode}' is not a recognized registration mode. Use 'Onsite' or 'Online'.");
        }

        var member = await db.Members.FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (member is null)
        {
            return Result<EventRegistrationDto>.Failure("No member profile found for this account.");
        }

        var @event = await db.Events.Include(e => e.Sessions).FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
        if (@event is null || @event.Status != EventStatus.Published)
        {
            // A Draft event isn't open for registration yet - treated as "doesn't exist", same as
            // EventService.GetByIdAsync's non-staff behavior, so a guessed/leaked id can't be used
            // to register before the event is actually published.
            return Result<EventRegistrationDto>.NotFound($"Event '{eventId}' was not found.");
        }

        var alreadyRegistered = await db.EventRegistrations.AnyAsync(
            r => r.EventId == eventId && r.MemberId == member.Id && r.Status != EventRegistrationStatus.Cancelled,
            cancellationToken);
        if (alreadyRegistered)
        {
            return Result<EventRegistrationDto>.Conflict("You're already registered for this event.");
        }

        var registration = new EventRegistration { EventId = eventId, MemberId = member.Id, Mode = parsedMode };
        db.EventRegistrations.Add(registration);
        await db.SaveChangesAsync(cancellationToken);

        return Result<EventRegistrationDto>.Success(
            ToRegistrationDto(registration, @event, member, sessionsAttended: 0, totalSessions: @event.Sessions.Count));
    }

    public async Task<Result> CancelRegistrationAsync(Guid userId, Guid registrationId, CancellationToken cancellationToken = default)
    {
        var registration = await db.EventRegistrations
            .Include(r => r.Member)
            .FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken);
        if (registration is null)
        {
            return Result.NotFound($"Registration '{registrationId}' was not found.");
        }
        if (registration.Member.UserId != userId)
        {
            return Result.Forbidden("This isn't your registration.");
        }

        // Once a payment is verified, cancelling would need refund handling - explicitly out of
        // scope (see proposal.md's "Not Built"). Before that point there's nothing to unwind.
        if (registration.Status is not (EventRegistrationStatus.Registered or EventRegistrationStatus.PaymentSubmitted or EventRegistrationStatus.Rejected))
        {
            return Result.Failure("This registration can no longer be cancelled.");
        }

        registration.Status = EventRegistrationStatus.Cancelled;
        registration.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Shared by every task that returns an EventRegistrationDto (this one, and Tasks
    /// 6-7's attendance/evaluation methods).</summary>
    private static EventRegistrationDto ToRegistrationDto(
        EventRegistration r, Event e, Member m, int sessionsAttended, int totalSessions) =>
        new(r.Id, r.EventId, e.Title, e.StartsAt, r.MemberId, $"{m.FirstName} {m.LastName}", m.MembershipNo,
            r.Mode.ToString(), r.Status.ToString(), sessionsAttended, totalSessions,
            r.EvaluationRating, r.EvaluationComments, r.EvaluationSubmittedAt,
            CpdCredit.For(r, e, sessionsAttended, totalSessions));
}
