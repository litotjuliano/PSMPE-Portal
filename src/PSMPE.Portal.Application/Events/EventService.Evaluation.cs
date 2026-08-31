using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Events;

public partial class EventService
{
    public async Task<Result> SubmitEvaluationAsync(
        Guid userId, Guid registrationId, int rating, string? comments, CancellationToken cancellationToken = default)
    {
        if (rating is < 1 or > 5)
        {
            return Result.Failure("Rating must be between 1 and 5.");
        }

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
        if (registration.Status != EventRegistrationStatus.Attended)
        {
            return Result.Failure("You need to be marked attended before you can submit the evaluation.");
        }

        registration.Status = EventRegistrationStatus.EvaluationSubmitted;
        registration.EvaluationRating = rating;
        registration.EvaluationComments = comments;
        registration.EvaluationSubmittedAt = DateTimeOffset.UtcNow;
        registration.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
