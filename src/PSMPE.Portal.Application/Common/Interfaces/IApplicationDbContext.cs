using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Application.Common.Interfaces;

/// <summary>
/// Abstraction over the EF Core DbContext so Application services never take a direct
/// dependency on Infrastructure/EF Core.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<ContentItem> ContentItems { get; }
    DbSet<Layout> Layouts { get; }
    DbSet<SystemConfig> SystemConfigs { get; }
    DbSet<Member> Members { get; }
    DbSet<MemberUpload> MemberUploads { get; }
    DbSet<PrcVerificationHistory> PrcVerificationHistories { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
