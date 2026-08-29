using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.Property(e => e.Title).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Description).HasMaxLength(4000);
        builder.Property(e => e.Objectives).HasMaxLength(4000);
        builder.Property(e => e.Type).HasMaxLength(64);
        builder.Property(e => e.Chapter).HasMaxLength(64);
        builder.Property(e => e.Venue).HasMaxLength(256);
        builder.Property(e => e.Hours).HasPrecision(6, 2);
        builder.Property(e => e.FeeOnsite).HasPrecision(12, 2);
        builder.Property(e => e.FeeOnline).HasPrecision(12, 2);
        builder.Property(e => e.CpdUnitsOnsite).HasPrecision(6, 2);
        builder.Property(e => e.CpdUnitsOnline).HasPrecision(6, 2);
        builder.Property(e => e.CpdCodeOnsite).HasMaxLength(64);
        builder.Property(e => e.CpdCodeOnline).HasMaxLength(64);
        builder.Property(e => e.PosterImageStorageKey).HasMaxLength(512);

        // The events list filters/sorts on StartsAt; the admin roster looks events up by id only.
        builder.HasIndex(e => e.StartsAt);
    }
}
