using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Events.Dtos;

namespace PSMPE.Portal.Application.Events;

public partial class EventService
{
    public async Task<Result<CertificateDataDto>> GetCertificateDataAsync(
        Guid userId, Guid registrationId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var registration = await db.EventRegistrations
            .Include(r => r.Member)
            .Include(r => r.Event).ThenInclude(e => e.Sessions)
            .FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken);
        if (registration is null)
        {
            return Result<CertificateDataDto>.NotFound($"Registration '{registrationId}' was not found.");
        }
        if (!isAdmin && registration.Member.UserId != userId)
        {
            return Result<CertificateDataDto>.Forbidden("This isn't your registration.");
        }

        var attendedSessionIds = await db.EventAttendances
            .Where(a => a.EventRegistrationId == registrationId)
            .Select(a => a.EventSessionId)
            .ToListAsync(cancellationToken);
        var totalSessions = registration.Event.Sessions.Count;
        var credit = CpdCredit.For(registration, registration.Event, attendedSessionIds.Count, totalSessions);
        if (credit is null)
        {
            return Result<CertificateDataDto>.Failure(
                "This registration hasn't earned CPD credit yet - the certificate isn't available.");
        }

        var attendedTitles = registration.Event.Sessions
            .Where(s => attendedSessionIds.Contains(s.Id))
            .OrderBy(s => s.Order)
            .Select(s => s.Title)
            .ToList();

        return Result<CertificateDataDto>.Success(new CertificateDataDto(
            $"{registration.Member.FirstName} {registration.Member.LastName}", registration.Event.Title,
            registration.Event.StartsAt, registration.Event.EndsAt, registration.Mode.ToString(),
            attendedTitles, credit.Value));
    }
}
