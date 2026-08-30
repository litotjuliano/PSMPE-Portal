using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.Application.Common.Configuration;

/// <summary>
/// Overrides a MembershipFeeKeys amount with an active FeePromotion's PromoAmount, when one covers
/// the given date, else falls back to the regular amount. A plain static function rather than a DI
/// service - the same reason MembershipGracePeriod is one: MemberService is constructed directly
/// (`new MemberService(db)`) by ~70 existing unit tests via an optional-cache-parameter trick, so a
/// required new dependency would break them, and PaymentService needs the identical resolution
/// logic. Deliberately does no caching of its own and does no date defaulting - the caller decides
/// both (PaymentService.GetFeesAsync wraps its whole read in one cached factory;
/// MemberService.EnsureRegistrationPaymentAsync reads uncached, matching how it already reads
/// SystemConfig inline today), so this stays a simple, directly-testable, side-effect-free lookup.
/// FeePromotion's creation-time overlap check guarantees at most one row ever matches for a given
/// FeeKey/date, so the first match is the only match.
/// </summary>
public static class FeePromotionResolver
{
    public static async Task<decimal> ResolveAsync(
        IApplicationDbContext db, string feeKey, decimal regularAmount, DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        var promoAmount = await db.FeePromotions.AsNoTracking()
            .Where(p => p.FeeKey == feeKey && p.StartDate <= asOf && p.EndDate >= asOf)
            .Select(p => (decimal?)p.PromoAmount)
            .FirstOrDefaultAsync(cancellationToken);

        return promoAmount ?? regularAmount;
    }
}
