using System.Globalization;
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

    /// <summary>
    /// The "single fee, as of today" shape: reads one SystemConfig row directly (no preloaded
    /// dictionary - callers with several keys to resolve at once, like PaymentService.GetFeesAsync
    /// and MemberService.EnsureRegistrationPaymentAsync, load their own dictionary and call
    /// ResolveAsync per key instead), falls back to <paramref name="fallback"/> when the row is
    /// missing or unparseable, then runs the result through the same date-range promo override as
    /// ResolveAsync. Extracted because PaymentService.SubmitAsync and
    /// MemberService.ResolveRegistrationPaymentAsync (the admin walk-in path) both need exactly this
    /// one-off "what does this single fee currently cost" lookup and had drifted into two
    /// near-identical copies of it.
    /// </summary>
    public static async Task<decimal> ResolveCurrentAsync(
        IApplicationDbContext db, string feeKey, decimal fallback, CancellationToken cancellationToken = default)
    {
        var raw = await db.SystemConfigs.AsNoTracking()
            .Where(c => c.Key == feeKey)
            .Select(c => c.Value)
            .FirstOrDefaultAsync(cancellationToken);

        var regularAmount = raw is not null
            && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

        return await ResolveAsync(db, feeKey, regularAmount, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
    }
}
