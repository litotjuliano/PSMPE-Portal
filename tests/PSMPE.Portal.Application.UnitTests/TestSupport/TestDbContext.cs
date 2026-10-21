using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Domain.Entities;

namespace PSMPE.Portal.Application.UnitTests.TestSupport;

/// <summary>In-memory-backed stand-in for ApplicationDbContext, used to unit test Application services
/// without pulling in the Infrastructure/Identity/Npgsql dependencies.</summary>
public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<ContentItem> ContentItems => Set<ContentItem>();
    public DbSet<Layout> Layouts => Set<Layout>();
    public DbSet<PhRegion> PhRegions => Set<PhRegion>();
    public DbSet<Chapter> Chapters => Set<Chapter>();
    public DbSet<PhProvince> PhProvinces => Set<PhProvince>();
    public DbSet<PhCity> PhCities => Set<PhCity>();
    public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<MemberUpload> MemberUploads => Set<MemberUpload>();
    public DbSet<MemberCertificate> MemberCertificates => Set<MemberCertificate>();
    public DbSet<PrcVerificationHistory> PrcVerificationHistories => Set<PrcVerificationHistory>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<FeePromotion> FeePromotions => Set<FeePromotion>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventSession> EventSessions => Set<EventSession>();
    public DbSet<EventRegistration> EventRegistrations => Set<EventRegistration>();
    public DbSet<EventAttendance> EventAttendances => Set<EventAttendance>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ErrorLog> ErrorLogs => Set<ErrorLog>();
    public DbSet<RenewalReminderLog> RenewalReminderLogs => Set<RenewalReminderLog>();

    public static TestDbContext CreateInMemory()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TestDbContext(options);
        db.Chapters.AddRange(TestChapters.BuildAll());
        db.SaveChanges();
        return db;
    }
}
