using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence.Configurations;

public class ErrorLogConfiguration : IEntityTypeConfiguration<ErrorLog>
{
    public void Configure(EntityTypeBuilder<ErrorLog> builder)
    {
        // Stored as text, matching MemberUploadConfiguration's/PaymentConfiguration's reasoning: an
        // int ordinal silently remaps every existing row if a value is ever inserted into the middle
        // of the enum.
        builder.Property(e => e.Source).HasConversion<string>().HasMaxLength(32);

        // Max lengths sized to what each column actually stores: type/path/method/url/user-agent
        // strings are bounded, StackTrace gets generous headroom for deep traces.
        builder.Property(e => e.ExceptionType).HasMaxLength(256);
        builder.Property(e => e.Message).IsRequired().HasMaxLength(2000);
        builder.Property(e => e.StackTrace).HasMaxLength(8000);
        builder.Property(e => e.RequestPath).HasMaxLength(512);
        builder.Property(e => e.RequestMethod).HasMaxLength(16);
        builder.Property(e => e.Url).HasMaxLength(512);
        builder.Property(e => e.UserAgent).HasMaxLength(512);

        // UserId: no FK to AspNetUsers - deliberately unenforced, since neither Restrict (would
        // block ever deleting a user who tripped a rate limit) nor Cascade (would silently delete
        // error history that must survive the user's deletion) is correct here.

        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => e.Source);
    }
}
