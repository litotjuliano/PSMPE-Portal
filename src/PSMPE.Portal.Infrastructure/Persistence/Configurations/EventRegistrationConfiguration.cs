using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class EventRegistrationConfiguration : IEntityTypeConfiguration<EventRegistration>
{
    public void Configure(EntityTypeBuilder<EventRegistration> builder)
    {
        builder.Property(r => r.EvaluationComments).HasMaxLength(2000);

        // Stored as text, matching Payment/MemberUpload's convention: an int ordinal silently
        // remaps every existing row if a value is ever inserted into the middle of the enum.
        builder.Property(r => r.Mode).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);

        // The roster query filters on EventId; "one non-cancelled registration per member per
        // event" is enforced in EventService, not by a DB constraint, since Cancelled rows must
        // stay queryable without blocking a fresh registration.
        builder.HasIndex(r => r.EventId);
        builder.HasIndex(r => r.MemberId);

        // Restrict, matching Payment.MemberId - deleting an Event or a Member must not silently
        // take registration history with it. Neither Event nor Member deletion exists in this
        // pass, but the FK still needs an explicit choice.
        builder.HasOne(r => r.Event)
            .WithMany()
            .HasForeignKey(r => r.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Member)
            .WithMany()
            .HasForeignKey(r => r.MemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
