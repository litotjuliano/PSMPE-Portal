using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class PhRegionConfiguration : IEntityTypeConfiguration<PhRegion>
{
    public void Configure(EntityTypeBuilder<PhRegion> builder)
    {
        builder.Property(r => r.Name).IsRequired().HasMaxLength(128);
        builder.HasIndex(r => r.Name).IsUnique();
    }
}

public class PhProvinceConfiguration : IEntityTypeConfiguration<PhProvince>
{
    public void Configure(EntityTypeBuilder<PhProvince> builder)
    {
        builder.Property(p => p.Name).IsRequired().HasMaxLength(128);
        builder.HasIndex(p => new { p.RegionId, p.Name }).IsUnique();
        builder.HasOne(p => p.Region).WithMany(r => r.Provinces)
            .HasForeignKey(p => p.RegionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PhCityConfiguration : IEntityTypeConfiguration<PhCity>
{
    public void Configure(EntityTypeBuilder<PhCity> builder)
    {
        builder.Property(c => c.Name).IsRequired().HasMaxLength(160);
        builder.Property(c => c.ZipCode).HasMaxLength(16);
        builder.HasIndex(c => new { c.ProvinceId, c.Name }).IsUnique();
        builder.HasOne(c => c.Province).WithMany(p => p.Cities)
            .HasForeignKey(c => c.ProvinceId).OnDelete(DeleteBehavior.Restrict);
    }
}
