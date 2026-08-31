using PSMPE.Portal.Application.Common.Configuration;
using PSMPE.Portal.Application.UnitTests.TestSupport;
using PSMPE.Portal.Domain.Entities;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Configuration;

/// <summary>
/// FeePromotionResolver is the pure lookup PaymentService.GetFeesAsync and
/// MemberService.EnsureRegistrationPaymentAsync both call through, so a promotion applies
/// identically wherever a fee is read. See FeePromotion for the overlap rule that keeps at most
/// one row matching a given FeeKey/date.
/// </summary>
public class FeePromotionResolverTests
{
    private const string FeeKey = "MembershipFee";
    private const decimal RegularAmount = 1500m;

    [Fact]
    public async Task ResolveAsync_WithAPromotionCoveringToday_ReturnsThePromoAmount()
    {
        using var db = TestDbContext.CreateInMemory();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.FeePromotions.Add(new FeePromotion
        {
            FeeKey = FeeKey,
            PromoAmount = 1000m,
            StartDate = today.AddDays(-1),
            EndDate = today.AddDays(1),
            CreatedByUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();

        var resolved = await FeePromotionResolver.ResolveAsync(db, FeeKey, RegularAmount, today);

        Assert.Equal(1000m, resolved);
    }

    [Fact]
    public async Task ResolveAsync_WithAPromotionOutsideItsDateRange_FallsBackToTheRegularAmount()
    {
        using var db = TestDbContext.CreateInMemory();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.FeePromotions.Add(new FeePromotion
        {
            FeeKey = FeeKey,
            PromoAmount = 1000m,
            StartDate = today.AddDays(-10),
            EndDate = today.AddDays(-2),
            CreatedByUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();

        var resolved = await FeePromotionResolver.ResolveAsync(db, FeeKey, RegularAmount, today);

        Assert.Equal(RegularAmount, resolved);
    }

    [Fact]
    public async Task ResolveAsync_WithNoMatchingPromotion_FallsBackToTheRegularAmount()
    {
        using var db = TestDbContext.CreateInMemory();

        var resolved = await FeePromotionResolver.ResolveAsync(
            db, FeeKey, RegularAmount, DateOnly.FromDateTime(DateTime.UtcNow));

        Assert.Equal(RegularAmount, resolved);
    }

    [Fact]
    public async Task ResolveAsync_WithAPromotionForADifferentFeeKey_IsIgnored()
    {
        using var db = TestDbContext.CreateInMemory();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.FeePromotions.Add(new FeePromotion
        {
            FeeKey = "MembershipShippingFee",
            PromoAmount = 50m,
            StartDate = today,
            EndDate = today,
            CreatedByUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();

        var resolved = await FeePromotionResolver.ResolveAsync(db, FeeKey, RegularAmount, today);

        Assert.Equal(RegularAmount, resolved);
    }
}
