using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(a => a.EventType).IsRequired().HasMaxLength(64);
        builder.Property(a => a.ActorIp).HasMaxLength(64);
        builder.Property(a => a.TargetType).HasMaxLength(64);

        // ActorUserId: no FK to AspNetUsers - deliberately unenforced, since neither Restrict
        // (would block ever deleting a user who tripped a rate limit) nor Cascade (would silently
        // delete audit history that must survive the user's deletion) is correct here.

        // The pruning job filters on EventType/CreatedAt; the Audit tab's Event Type filter and
        // date range filter drive the same two columns.
        builder.HasIndex(a => a.CreatedAt);
        builder.HasIndex(a => a.EventType);
    }
}
