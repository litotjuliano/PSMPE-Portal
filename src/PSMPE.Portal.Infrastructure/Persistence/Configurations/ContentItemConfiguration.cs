using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class ContentItemConfiguration : IEntityTypeConfiguration<ContentItem>
{
    public void Configure(EntityTypeBuilder<ContentItem> builder)
    {
        builder.Property(c => c.Title).IsRequired().HasMaxLength(256);
        builder.Property(c => c.Body).IsRequired();

        builder.HasIndex(c => c.OwnerId);

        builder.HasOne(c => c.Owner)
            .WithMany()
            .HasForeignKey(c => c.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Layout)
            .WithMany()
            .HasForeignKey(c => c.LayoutId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
