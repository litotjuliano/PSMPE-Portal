namespace PSMPE.Portal.Application.Events.Dtos;

/// <summary>
/// EventType/Hours/CpdCode were added alongside Event.Type/Event.Hours/Event.CpdCodeOnsite/
/// Event.CpdCodeOnline (add-events-cpd-tracker/proposal.md's 2026-08-29 revision) - CpdCode is the
/// PRC accreditation reference for the registration's Mode, resolved by CpdCredit.CodeFor the same
/// way CreditUnits is resolved by CpdCredit.For. Event.Objectives is deliberately left off: it's
/// long-form informational text (shown on the event detail view instead) that doesn't fit a
/// one-page certificate.
/// </summary>
public record CertificateDataDto(
    string MemberName,
    string EventTitle,
    DateTimeOffset EventStartsAt,
    DateTimeOffset EventEndsAt,
    string Mode,
    IReadOnlyList<string> AttendedSessionTitles,
    decimal CreditUnits,
    string? EventType,
    decimal? Hours,
    string? CpdCode);
