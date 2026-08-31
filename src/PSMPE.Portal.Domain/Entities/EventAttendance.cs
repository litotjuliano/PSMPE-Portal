namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// One row per EventSession a registrant is confirmed to have attended - what "attended" means
/// structurally in this design. Recorded by an Admin during post-event roster reconciliation, never
/// by the member themselves (there is no member self-check-in in this product - see
/// add-events-cpd-tracker/proposal.md). RecordedBy/RecordedAt are an audit trail of who reconciled
/// it and when, mirroring Payment.DecidedByUserId/DecidedAt.
/// </summary>
public class EventAttendance : BaseEntity
{
    public Guid EventRegistrationId { get; set; }
    public EventRegistration EventRegistration { get; set; } = null!;

    public Guid EventSessionId { get; set; }
    public EventSession EventSession { get; set; } = null!;

    public Guid RecordedBy { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}
