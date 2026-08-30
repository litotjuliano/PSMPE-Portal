namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// A time-boxed override of a MembershipFeeKeys amount - e.g. MembershipFee at PromoAmount instead
/// of its configured SystemConfig value, for a single outreach-event day. Resolved on every fee
/// read (a later task) purely by comparing today's date to StartDate/EndDate rather than by a
/// background job, so a promotion starts and stops by itself with nothing to schedule or forget to
/// revert. Overlapping promotions for the same FeeKey are rejected at creation (a later task), so
/// at most one is ever active for a given fee on a given day. CreatedAt (from BaseEntity) is this
/// row's own audit timestamp.
/// </summary>
public class FeePromotion : BaseEntity
{
    /// <summary>One of the MembershipFeeKeys constants (e.g. "MembershipFee", "PortalFee"). Not a
    /// foreign key - SystemConfig rows are themselves looked up by this same string key, so a
    /// promotion is matched to a fee the same way the fee itself is.</summary>
    public string FeeKey { get; set; } = string.Empty;

    /// <summary>The discounted amount in effect for FeeKey while today falls within
    /// StartDate..EndDate, in place of the regular SystemConfig value.</summary>
    public decimal PromoAmount { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    /// <summary>Admin who created this promotion. Bare Guid with no navigation property, matching
    /// Payment.DecidedByUserId's pattern for "who did this" audit fields.</summary>
    public Guid CreatedByUserId { get; set; }
}
