namespace PSMPE.Portal.Application.Common.Configuration;

/// <summary>
/// The SystemConfig keys holding PSMPE's membership fees, and their fallbacks.
///
/// These were hardcoded constants in ReceiptGenerator and a literal "TOTAL: ₱1,700.00" in the
/// registration wizard. Fees change; a code deploy to alter one is the wrong shape, and having the
/// same figure in two places guarantees they eventually disagree.
///
/// The defaults are what shipped as constants, so a database missing these rows behaves exactly as
/// before rather than charging zero.
/// </summary>
public static class MembershipFeeKeys
{
    public const string MembershipFee = "MembershipFee";
    public const string ShippingFee = "MembershipShippingFee";
    public const string AnnualDues = "AnnualDues";

    public const decimal DefaultMembershipFee = 1500m;
    public const decimal DefaultShippingFee = 200m;
    public const decimal DefaultAnnualDues = 600m;

    /// <summary>Single cache entry for all three - they are always read together, and one entry
    /// means one thing to evict when an admin edits them.</summary>
    public const string CacheKey = "config:membership-fees";

    public static readonly (string Key, decimal Default, string Description)[] All =
    [
        (MembershipFee, DefaultMembershipFee, "One-time membership fee charged at registration, in PHP."),
        (ShippingFee, DefaultShippingFee, "One-time ID/card shipping fee charged at registration, in PHP."),
        (AnnualDues, DefaultAnnualDues, "Annual dues payable each year on the member's renewal date, in PHP."),
    ];
}
