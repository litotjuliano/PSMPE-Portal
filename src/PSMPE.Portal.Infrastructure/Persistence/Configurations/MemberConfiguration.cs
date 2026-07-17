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
        builder.Property(m => m.PtrNumber).HasMaxLength(64);
        builder.Property(m => m.Tin).HasMaxLength(32);
        builder.Property(m => m.CivilStatus).HasMaxLength(32);
        builder.Property(m => m.Company).HasMaxLength(256);
        builder.Property(m => m.Address).HasMaxLength(512);
        builder.Property(m => m.MobileNumber).HasMaxLength(32);
        builder.Property(m => m.NationalDuesReferenceNo).HasMaxLength(64);
        builder.Property(m => m.PrcIdVerified).IsRequired();
        builder.Property(m => m.PendingPrcLicenseNo).HasMaxLength(64);
        builder.Property(m => m.PrcVerificationRejectedReason).HasMaxLength(512);

        builder.Property(m => m.HousePhone).HasMaxLength(32);
        builder.Property(m => m.Website).HasMaxLength(256);
        builder.Property(m => m.FacebookUrl).HasMaxLength(256);
        builder.Property(m => m.LinkedInUrl).HasMaxLength(256);
        builder.Property(m => m.XUrl).HasMaxLength(256);
        builder.Property(m => m.InstagramUrl).HasMaxLength(256);

        builder.Property(m => m.EmploymentStatus).HasMaxLength(32);
        builder.Property(m => m.Position).HasMaxLength(128);
        builder.Property(m => m.BusinessAddress).HasMaxLength(512);
        builder.Property(m => m.Specialization).HasMaxLength(256);
        builder.Property(m => m.Skills).HasMaxLength(512);

        builder.HasIndex(m => m.UserId).IsUnique();
        builder.HasIndex(m => m.MembershipNo).IsUnique();

        builder.HasOne(m => m.User)
            .WithOne()
            .HasForeignKey<Member>(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
