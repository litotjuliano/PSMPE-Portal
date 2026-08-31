namespace PSMPE.Portal.Application.Events.Dtos;

public record MyCpdRegistrationDto(
    Guid RegistrationId,
    Guid EventId,
    string EventTitle,
    DateTimeOffset EventStartsAt,
    string Mode,
    string Status,
    int SessionsAttended,
    int TotalSessions,
    decimal? CreditUnits);

public record MyCpdSummaryDto(decimal TotalCreditUnits, IReadOnlyList<MyCpdRegistrationDto> Registrations);
