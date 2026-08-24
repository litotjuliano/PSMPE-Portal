using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        // 12,2 rather than the provider default: money needs an exact decimal, and the fees this
        // records are in pesos with centavos.
        builder.Property(p => p.Amount).HasPrecision(12, 2);
        builder.Property(p => p.ReferenceNo).HasMaxLength(64);
        builder.Property(p => p.ProofStorageKey).HasMaxLength(512);
        builder.Property(p => p.RejectedReason).HasMaxLength(512);

        // Stored as text, matching MemberUploadConfiguration's reasoning: an int ordinal silently
        // remaps every existing row if a value is ever inserted into the middle of the enum.
        builder.Property(p => p.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32);

        // The admin queue filters on Status and orders oldest-first; the member's own history
        // filters on MemberId.
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.MemberId);

        // Restrict, same as PrcVerificationHistory - deleting a member must not silently take their
        // payment record with it. MemberService.DeleteAsync pre-checks and fails cleanly.
        builder.HasOne(p => p.Member)
            .WithMany()
            .HasForeignKey(p => p.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict, same reasoning as MemberId - a registration with payment history shouldn't
        // vanish out from under its payment row.
        builder.HasOne(p => p.EventRegistration)
            .WithMany()
            .HasForeignKey(p => p.EventRegistrationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
