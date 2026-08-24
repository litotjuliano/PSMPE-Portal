using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IApplicationDbContext
{
    public DbSet<ContentItem> ContentItems => Set<ContentItem>();
    public DbSet<Layout> Layouts => Set<Layout>();
    public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<MemberUpload> MemberUploads => Set<MemberUpload>();
    public DbSet<MemberCertificate> MemberCertificates => Set<MemberCertificate>();
    public DbSet<PrcVerificationHistory> PrcVerificationHistories => Set<PrcVerificationHistory>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventSession> EventSessions => Set<EventSession>();
    public DbSet<EventRegistration> EventRegistrations => Set<EventRegistration>();
    public DbSet<EventAttendance> EventAttendances => Set<EventAttendance>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ErrorLog> ErrorLogs => Set<ErrorLog>();
    public DbSet<RenewalReminderLog> RenewalReminderLogs => Set<RenewalReminderLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
