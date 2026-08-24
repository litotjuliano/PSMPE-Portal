using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class EventAttendanceConfiguration : IEntityTypeConfiguration<EventAttendance>
{
    public void Configure(EntityTypeBuilder<EventAttendance> builder)
    {
        // Defensive: EventService.RecordAttendanceAsync always fully replaces a registration's
        // attendance rows in one call rather than upserting, so this should never fire in
        // practice, but a duplicate (registration, session) pair would silently double-count
        // toward "sessions attended" if it ever did.
        builder.HasIndex(a => new { a.EventRegistrationId, a.EventSessionId }).IsUnique();

        // Cascade - an attendance row has no meaning once its registration is gone, mirroring
        // EventSessionConfiguration's reasoning for Event -> EventSession.
        builder.HasOne(a => a.EventRegistration)
            .WithMany()
            .HasForeignKey(a => a.EventRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict - unlike Event -> EventSession, a session with recorded attendance must not be
        // removable out from under that history. EventService.UpdateAsync checks this explicitly
        // before attempting the delete (see Task 4), so this is a defense-in-depth constraint, not
        // the primary guard.
        builder.HasOne(a => a.EventSession)
            .WithMany()
            .HasForeignKey(a => a.EventSessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
