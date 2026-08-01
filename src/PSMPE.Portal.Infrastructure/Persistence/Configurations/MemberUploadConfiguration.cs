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

        // Stored as the enum member's name, not its ordinal - an int column silently
        // reinterprets every existing row's meaning if UploadKind is ever reordered/a value is
        // removed from the middle (this bit us once already: removing FormalPhoto shifted
        // Signature/ProofOfPayment's ordinals for pre-existing rows). A string column is immune
        // to that class of bug regardless of how the enum's declaration order changes later.
        builder.Property(u => u.Kind).HasConversion<string>().HasMaxLength(32);

        // One row per (UserId, Kind) - re-uploading replaces the pointer (and the underlying
        // file at the same storage key), no accumulation of stale rows/files.
        builder.HasIndex(u => new { u.UserId, u.Kind }).IsUnique();
    }
}
