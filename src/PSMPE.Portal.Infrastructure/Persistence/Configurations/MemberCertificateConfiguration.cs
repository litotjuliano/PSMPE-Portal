using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class MemberCertificateConfiguration : IEntityTypeConfiguration<MemberCertificate>
{
    public void Configure(EntityTypeBuilder<MemberCertificate> builder)
    {
        builder.Property(c => c.FileName).IsRequired().HasMaxLength(256);
        builder.Property(c => c.StorageKey).IsRequired().HasMaxLength(512);
        builder.Property(c => c.ContentType).IsRequired().HasMaxLength(128);

        // Not unique - a member can have any number of certificates.
        builder.HasIndex(c => c.UserId);
    }
}
