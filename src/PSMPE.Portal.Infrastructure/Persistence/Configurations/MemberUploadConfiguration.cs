using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class MemberUploadConfiguration : IEntityTypeConfiguration<MemberUpload>
{
    public void Configure(EntityTypeBuilder<MemberUpload> builder)
    {
        builder.Property(u => u.StorageKey).IsRequired().HasMaxLength(512);
        builder.Property(u => u.ContentType).IsRequired().HasMaxLength(128);

        // One row per (UserId, Kind) - re-uploading replaces the pointer (and the underlying
        // file at the same storage key), no accumulation of stale rows/files.
        builder.HasIndex(u => new { u.UserId, u.Kind }).IsUnique();
    }
}
