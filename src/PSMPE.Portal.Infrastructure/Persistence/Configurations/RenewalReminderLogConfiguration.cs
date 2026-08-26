using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class RenewalReminderLogConfiguration : IEntityTypeConfiguration<RenewalReminderLog>
{
    public void Configure(EntityTypeBuilder<RenewalReminderLog> builder)
    {
        // Stored as text, matching PaymentConfiguration's reasoning: an int ordinal silently remaps
        // every existing row if a value is ever inserted into the middle of the enum.
        builder.Property(r => r.ReminderType).HasConversion<string>().HasMaxLength(32);

        // The idempotency guarantee itself - MembershipLifecycleService's pre-check race is closed
        // by this constraint, not just the check.
        builder.HasIndex(r => new { r.MemberId, r.ReminderType, r.ForRenewalDueDate }).IsUnique();

        // Cascade, unlike Payment/PrcVerificationHistory's Restrict - this is disposable idempotency
        // bookkeeping, not a financial or decision record worth preserving against member deletion.
        builder.HasOne<Member>()
            .WithMany()
            .HasForeignKey(r => r.MemberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
