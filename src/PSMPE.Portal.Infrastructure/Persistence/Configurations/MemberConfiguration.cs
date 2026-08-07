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
        // Not IsRequired: assigned by an admin at approval, null before that. The unique index
        // below still applies - Postgres treats NULLs as distinct, so applicants awaiting a number
        // don't conflict.
        builder.Property(m => m.MembershipNo).HasMaxLength(32);
        builder.Property(m => m.Chapter).IsRequired().HasMaxLength(64);
        builder.Property(m => m.MemberType).IsRequired().HasMaxLength(64);
        builder.Property(m => m.PrcLicenseNo).HasMaxLength(64);
        builder.Property(m => m.PtrNumber).HasMaxLength(64);
        builder.Property(m => m.Tin).HasMaxLength(32);
        builder.Property(m => m.CivilStatus).HasMaxLength(32);
        builder.Property(m => m.Company).HasMaxLength(256);
        builder.Property(m => m.MobileNumber).HasMaxLength(32);
        builder.Property(m => m.NationalDuesReferenceNo).HasMaxLength(64);
        builder.Property(m => m.PrcIdVerified).IsRequired();
        builder.Property(m => m.PendingPrcLicenseNo).HasMaxLength(64);
        builder.Property(m => m.PrcVerificationRejectedReason).HasMaxLength(512);

        // Residence address - replaces the old single free-text Address field.
        builder.Property(m => m.HouseNo).HasMaxLength(32);
        builder.Property(m => m.Street).HasMaxLength(128);
        builder.Property(m => m.Barangay).HasMaxLength(128);
        builder.Property(m => m.CityMunicipality).HasMaxLength(128);
        builder.Property(m => m.Province).HasMaxLength(64);
        builder.Property(m => m.ZipCode).HasMaxLength(8);

        // Mailing address - same shape as residence.
        builder.Property(m => m.MailingHouseNo).HasMaxLength(32);
        builder.Property(m => m.MailingStreet).HasMaxLength(128);
        builder.Property(m => m.MailingBarangay).HasMaxLength(128);
        builder.Property(m => m.MailingCityMunicipality).HasMaxLength(128);
        builder.Property(m => m.MailingProvince).HasMaxLength(64);
        builder.Property(m => m.MailingZipCode).HasMaxLength(8);

        // Educational record.
        builder.Property(m => m.EducationLevel).HasMaxLength(32);
        builder.Property(m => m.SchoolName).HasMaxLength(256);
        builder.Property(m => m.CourseYearGraduated).HasMaxLength(128);

        builder.Property(m => m.SpecifiedProfession).HasMaxLength(64);

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
