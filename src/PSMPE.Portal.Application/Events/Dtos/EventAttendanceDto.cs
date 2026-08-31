namespace PSMPE.Portal.Application.Events.Dtos;

/// <summary>One registrant's authoritative set of attended sessions for this call - SessionIds
/// fully replaces whatever EventAttendance rows already exist for RegistrationId, so re-running
/// reconciliation with a corrected set is how a mistake gets fixed (see spec.md's "admin reconciles
/// roster attendance" scenarios).</summary>
public record RegistrantAttendanceRequest(Guid RegistrationId, IReadOnlyList<Guid> SessionIds);

/// <summary>The request body for POST /api/events/{id}/roster/attendance - one call reconciles the
/// whole roster, not just one registrant, since that's how an admin actually works through a
/// printed sign-in sheet.</summary>
public record RecordAttendanceRequest(IReadOnlyList<RegistrantAttendanceRequest> Registrants);
