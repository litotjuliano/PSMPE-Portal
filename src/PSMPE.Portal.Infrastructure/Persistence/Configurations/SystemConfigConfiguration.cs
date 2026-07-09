using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class SystemConfigConfiguration : IEntityTypeConfiguration<SystemConfig>
{
    public void Configure(EntityTypeBuilder<SystemConfig> builder)
    {
        builder.Property(c => c.Key).IsRequired().HasMaxLength(128);
        builder.HasIndex(c => c.Key).IsUnique();
        builder.Property(c => c.Value).IsRequired();
    }
}
