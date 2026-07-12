using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class PrcVerificationHistoryConfiguration : IEntityTypeConfiguration<PrcVerificationHistory>
{
    public void Configure(EntityTypeBuilder<PrcVerificationHistory> builder)
    {
        builder.Property(h => h.OldValue).HasMaxLength(64);
        builder.Property(h => h.NewValue).HasMaxLength(64);
        builder.Property(h => h.DocumentStorageKey).HasMaxLength(512);
        builder.Property(h => h.Reason).HasMaxLength(512);

        builder.HasIndex(h => h.MemberId);
    }
}
