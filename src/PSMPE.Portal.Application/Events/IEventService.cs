using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Events.Dtos;

namespace PSMPE.Portal.Application.Events;

public interface IEventService
{
    Task<PagedResult<EventDto>> GetAllAsync(
        int page, int pageSize, string? search, string? chapter, bool upcomingOnly,
        CancellationToken cancellationToken = default);

    Task<EventDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<EventDto>> CreateAsync(CreateEventRequest request, CancellationToken cancellationToken = default);

    Task<Result<EventDto>> UpdateAsync(Guid id, UpdateEventRequest request, CancellationToken cancellationToken = default);

    Task<Result<EventRegistrationDto>> RegisterAsync(Guid userId, Guid eventId, string mode, CancellationToken cancellationToken = default);

    Task<Result> CancelRegistrationAsync(Guid userId, Guid registrationId, CancellationToken cancellationToken = default);

    Task<Result> SubmitEvaluationAsync(Guid userId, Guid registrationId, int rating, string? comments, CancellationToken cancellationToken = default);

    Task<Result> RecordAttendanceAsync(
        Guid eventId, IReadOnlyList<RegistrantAttendanceRequest> registrants, Guid adminUserId, CancellationToken cancellationToken = default);
}
