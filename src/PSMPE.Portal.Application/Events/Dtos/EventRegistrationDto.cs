namespace PSMPE.Portal.Application.Events.Dtos;

/// <summary>Mode/Status are strings (enum.ToString()), not the raw enums - see the design note at
/// the top of tasks.md: this is deliberately serialized so the frontend's string literal types
/// actually match what's sent over the wire, unlike PaymentDto.Kind/Status.</summary>
public record EventRegistrationDto(
    Guid Id,
    Guid EventId,
    string EventTitle,
    DateTimeOffset EventStartsAt,
    Guid MemberId,
    string MemberName,
    string? MembershipNo,
    string Mode,
    string Status,
    int SessionsAttended,
    int TotalSessions,
    int? EvaluationRating,
    string? EvaluationComments,
    DateTimeOffset? EvaluationSubmittedAt,
    /// <summary>Null until this registration reaches EvaluationSubmitted with a non-null unit
    /// value for its Mode - see Application/Events/CpdCredit.cs.</summary>
    decimal? CreditUnits);

public record RegisterForEventRequest(string Mode);
