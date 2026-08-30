using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class FeePromotionConfiguration : IEntityTypeConfiguration<FeePromotion>
{
    public void Configure(EntityTypeBuilder<FeePromotion> builder)
    {
        builder.Property(p => p.FeeKey).IsRequired().HasMaxLength(128);

        // 12,2 rather than the provider default: money needs an exact decimal, same reasoning as
        // PaymentConfiguration's Amount.
        builder.Property(p => p.PromoAmount).HasPrecision(12, 2);

        // Fee resolution (a later task) queries "WHERE FeeKey = X AND StartDate <= today AND
        // EndDate >= today" on every fee read - a composite index keeps that cheap instead of
        // scanning every promotion ever created, and doubles as the lookup overlap checks will use.
        builder.HasIndex(p => new { p.FeeKey, p.StartDate, p.EndDate });
    }
}
