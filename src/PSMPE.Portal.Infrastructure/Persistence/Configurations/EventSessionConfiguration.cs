using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class EventSessionConfiguration : IEntityTypeConfiguration<EventSession>
{
    public void Configure(EntityTypeBuilder<EventSession> builder)
    {
        builder.Property(s => s.Title).IsRequired().HasMaxLength(256);

        builder.HasIndex(s => s.EventId);

        // Cascade, unlike every other FK in this feature - a session has no meaning outside its
        // event, so EventService.UpdateAsync's session reconciliation (add/edit/remove lectures)
        // is the only thing that ever removes one, and removing an Event's row entirely (not
        // supported by any endpoint in this pass) should take its sessions with it rather than
        // leaving them orphaned.
        builder.HasOne(s => s.Event)
            .WithMany(e => e.Sessions)
            .HasForeignKey(s => s.EventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
