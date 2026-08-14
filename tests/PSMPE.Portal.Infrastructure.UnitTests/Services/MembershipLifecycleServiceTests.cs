using Microsoft.Extensions.Logging.Abstractions;
using PSMPE.Portal.Application.Common.Caching;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.Infrastructure.Services;
using PSMPE.Portal.Infrastructure.UnitTests.TestSupport;
using Xunit;

namespace PSMPE.Portal.Infrastructure.UnitTests.Services;

/// <summary>
/// Covers MembershipLifecycleService.ProcessDailyAsync: the daily reminder emails at 30/7/0 days
/// before RenewalDueDate plus one grace-period reminder, their idempotency, and the bulk auto-flip
/// of Status Active -> Expired once the grace period ends. Backed by SQLite (see
/// SqliteApplicationDbContextFactory) rather than EF Core InMemory, because the auto-flip uses
/// ExecuteUpdateAsync, which InMemory doesn't support.
/// </summary>
public class MembershipLifecycleServiceTests : IDisposable
{
    private const int GraceDays = 7;
    private readonly SqliteApplicationDbContextFactory _dbFactory = new();

    public void Dispose() => _dbFactory.Dispose();

    private static async Task SeedGracePeriodConfigAsync(ApplicationDbContext db)
    {
        db.SystemConfigs.Add(new SystemConfig { Key = "MembershipGracePeriodDays", Value = GraceDays.ToString() });
        await db.SaveChangesAsync();
    }

    private static Member BuildActiveMember(DateOnly? renewalDueDate, string? email = null) => new()
    {
        UserId = Guid.NewGuid(),
        User = new ApplicationUser
        {
            UserName = email ?? $"{Guid.NewGuid()}@example.com",
            Email = email ?? $"{Guid.NewGuid()}@example.com",
        },
        FirstName = "Juan",
        LastName = "Dela Cruz",
        Chapter = "NCR",
        MemberType = "Regular",
        Status = MembershipStatus.Active,
        SubmittedAt = DateTimeOffset.UtcNow.AddYears(-1),
        RenewalDueDate = renewalDueDate,
    };

    private MembershipLifecycleService CreateService(ApplicationDbContext db, RecordingEmailSender emailSender, FakeDateTimeProvider clock) =>
        new(db, NoOpCacheService.Instance, emailSender, clock, NullLogger<MembershipLifecycleService>.Instance);

    [Theory]
    [InlineData(30)] // 30 days before due
    [InlineData(7)] // 7 days before due
    [InlineData(0)] // due today
    [InlineData(-1)] // first day of grace
    public async Task ProcessDailyAsync_MemberAtReminderBoundary_SendsExactlyOneEmail_AndLogsIt(int dueDateOffsetDays)
    {
        await using var db = _dbFactory.CreateContext();
        await SeedGracePeriodConfigAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var member = BuildActiveMember(today.AddDays(dueDateOffsetDays));
        db.Members.Add(member);
        await db.SaveChangesAsync();

        var emailSender = new RecordingEmailSender();
        var service = CreateService(db, emailSender, new FakeDateTimeProvider(DateTimeOffset.UtcNow));

        await service.ProcessDailyAsync();

        Assert.Single(emailSender.Sent);
        Assert.Equal(member.User.Email, emailSender.Sent[0].To);
        var logRow = Assert.Single(db.RenewalReminderLogs);
        Assert.Equal(member.Id, logRow.MemberId);
        Assert.Equal(member.RenewalDueDate!.Value, logRow.ForRenewalDueDate);
    }

    [Theory]
    [InlineData(29)]
    [InlineData(31)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(-2)]
    public async Task ProcessDailyAsync_MemberNotAtABoundary_SendsNothing(int dueDateOffsetDays)
    {
        await using var db = _dbFactory.CreateContext();
        await SeedGracePeriodConfigAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var member = BuildActiveMember(today.AddDays(dueDateOffsetDays));
        db.Members.Add(member);
        await db.SaveChangesAsync();

        var emailSender = new RecordingEmailSender();
        var service = CreateService(db, emailSender, new FakeDateTimeProvider(DateTimeOffset.UtcNow));

        await service.ProcessDailyAsync();

        Assert.Empty(emailSender.Sent);
        Assert.Empty(db.RenewalReminderLogs);
    }

    [Fact]
    public async Task ProcessDailyAsync_RunTwiceSameDay_DoesNotDoubleSend()
    {
        await using var db = _dbFactory.CreateContext();
        await SeedGracePeriodConfigAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var member = BuildActiveMember(today.AddDays(-1)); // first day of grace - a trigger day
        db.Members.Add(member);
        await db.SaveChangesAsync();

        var emailSender = new RecordingEmailSender();
        var clock = new FakeDateTimeProvider(DateTimeOffset.UtcNow);
        var service = CreateService(db, emailSender, clock);

        await service.ProcessDailyAsync();
        await service.ProcessDailyAsync();

        Assert.Single(emailSender.Sent);
        Assert.Single(db.RenewalReminderLogs);
    }

    [Fact]
    public async Task ProcessDailyAsync_AfterRenewalDueDateAdvancesToNewCycle_ReminderFiresAgain()
    {
        await using var db = _dbFactory.CreateContext();
        await SeedGracePeriodConfigAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var member = BuildActiveMember(today.AddDays(-1)); // first day of grace - triggers the GracePeriod reminder
        db.Members.Add(member);
        await db.SaveChangesAsync();

        var emailSender = new RecordingEmailSender();
        var clock = new FakeDateTimeProvider(DateTimeOffset.UtcNow);
        var service = CreateService(db, emailSender, clock);
        await service.ProcessDailyAsync();
        Assert.Single(emailSender.Sent);

        // Simulate a renewal payment advancing RenewalDueDate to a new cycle (retroactive renewal
        // keeps the anniversary fixed - see PaymentVerification.Apply) and the clock moving forward
        // by the exact same span, so "today" lands on the identical one-day-past-due boundary again.
        var jump = TimeSpan.FromDays(400);
        member.RenewalDueDate = member.RenewalDueDate!.Value.AddDays(jump.Days);
        await db.SaveChangesAsync();
        clock.Advance(jump);

        await service.ProcessDailyAsync();

        Assert.Equal(2, emailSender.Sent.Count);
        Assert.Equal(2, db.RenewalReminderLogs.Count());
    }

    [Fact]
    public async Task ProcessDailyAsync_OneMemberEmailFails_OtherMembersStillProcessed()
    {
        await using var db = _dbFactory.CreateContext();
        await SeedGracePeriodConfigAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var failing = BuildActiveMember(today, email: "fails@example.com");
        var succeeding = BuildActiveMember(today, email: "ok@example.com");
        db.Members.AddRange(failing, succeeding);
        await db.SaveChangesAsync();

        var emailSender = new RecordingEmailSender();
        emailSender.ThrowWhenSendingTo("fails@example.com");
        var service = CreateService(db, emailSender, new FakeDateTimeProvider(DateTimeOffset.UtcNow));

        await service.ProcessDailyAsync();

        Assert.Single(emailSender.Sent);
        Assert.Equal("ok@example.com", emailSender.Sent[0].To);
        Assert.Single(db.RenewalReminderLogs);
        Assert.Equal(succeeding.Id, db.RenewalReminderLogs.Single().MemberId);
    }

    [Fact]
    public async Task ProcessDailyAsync_FlipsStatusToExpired_OnlyPastGracePeriod()
    {
        await using var db = _dbFactory.CreateContext();
        await SeedGracePeriodConfigAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var stillInGrace = BuildActiveMember(today.AddDays(-GraceDays)); // last day of grace - stays Active
        var justPastGrace = BuildActiveMember(today.AddDays(-GraceDays - 1)); // first expired day - flips
        var wellPastGrace = BuildActiveMember(today.AddDays(-100)); // flips
        var notYetDue = BuildActiveMember(today.AddDays(10)); // untouched
        db.Members.AddRange(stillInGrace, justPastGrace, wellPastGrace, notYetDue);
        await db.SaveChangesAsync();

        var emailSender = new RecordingEmailSender();
        var service = CreateService(db, emailSender, new FakeDateTimeProvider(DateTimeOffset.UtcNow));

        await service.ProcessDailyAsync();

        // Fresh context re-read, proving the change was actually persisted, not just tracked.
        await using var verifyDb = _dbFactory.CreateContext();
        Assert.Equal(MembershipStatus.Active, verifyDb.Members.Single(m => m.Id == stillInGrace.Id).Status);
        Assert.Equal(MembershipStatus.Expired, verifyDb.Members.Single(m => m.Id == justPastGrace.Id).Status);
        Assert.Equal(MembershipStatus.Expired, verifyDb.Members.Single(m => m.Id == wellPastGrace.Id).Status);
        Assert.Equal(MembershipStatus.Active, verifyDb.Members.Single(m => m.Id == notYetDue.Id).Status);
    }
}
