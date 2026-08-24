namespace PSMPE.Portal.Application.Events.Dtos;

public record CertificateDataDto(
    string MemberName,
    string EventTitle,
    DateTimeOffset EventStartsAt,
    DateTimeOffset EventEndsAt,
    string Mode,
    IReadOnlyList<string> AttendedSessionTitles,
    decimal CreditUnits);
