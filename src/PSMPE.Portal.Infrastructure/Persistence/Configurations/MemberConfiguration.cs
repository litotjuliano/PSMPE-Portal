using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.Property(m => m.FirstName).IsRequired().HasMaxLength(128);
        builder.Property(m => m.LastName).IsRequired().HasMaxLength(128);
        builder.Property(m => m.MiddleName).HasMaxLength(128);
        builder.Property(m => m.Suffix).HasMaxLength(32);
        builder.Property(m => m.MembershipNo).IsRequired().HasMaxLength(32);
        builder.Property(m => m.Chapter).IsRequired().HasMaxLength(64);
        builder.Property(m => m.MemberType).IsRequired().HasMaxLength(64);
        builder.Property(m => m.PrcLicenseNo).HasMaxLength(64);
        builder.Property(m => m.Company).HasMaxLength(256);
        builder.Property(m => m.Address).HasMaxLength(512);
        builder.Property(m => m.NationalDuesReferenceNo).HasMaxLength(64);
        builder.Property(m => m.PrcIdVerified).IsRequired();
        builder.Property(m => m.PendingPrcLicenseNo).HasMaxLength(64);
        builder.Property(m => m.PrcVerificationRejectedReason).HasMaxLength(512);

        builder.HasIndex(m => m.UserId).IsUnique();
        builder.HasIndex(m => m.MembershipNo).IsUnique();

        builder.HasOne(m => m.User)
            .WithOne()
            .HasForeignKey<Member>(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
