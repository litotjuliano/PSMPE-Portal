using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// One row per renewal reminder email actually sent, so MembershipLifecycleService's daily tick
/// never double-sends. CreatedAt (from BaseEntity) doubles as "sent at" - this row is never updated.
///
/// ForRenewalDueDate - the member's RenewalDueDate at send time, not "today" - is what the
/// idempotency key is scoped to rather than just (MemberId, ReminderType). That is what makes
/// reminders reset automatically once a renewal payment advances RenewalDueDate to a new cycle,
/// with no cleanup job needed.
/// </summary>
public class RenewalReminderLog : BaseEntity
{
    public Guid MemberId { get; set; }
    public RenewalReminderType ReminderType { get; set; }
    public DateOnly ForRenewalDueDate { get; set; }
}
