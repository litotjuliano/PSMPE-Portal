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
    DbSet<MemberCertificate> MemberCertificates { get; }
    DbSet<PrcVerificationHistory> PrcVerificationHistories { get; }
    DbSet<Payment> Payments { get; }
    DbSet<Event> Events { get; }
    DbSet<EventSession> EventSessions { get; }
    DbSet<EventRegistration> EventRegistrations { get; }
    DbSet<EventAttendance> EventAttendances { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<ErrorLog> ErrorLogs { get; }
    DbSet<RenewalReminderLog> RenewalReminderLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
