using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Infrastructure.Services;

/// <summary>
/// Daily membership housekeeping - see IMembershipLifecycleService. Two independent halves per
/// tick: reminder emails (idempotent via RenewalReminderLog) and the Status auto-flip (a single
/// bulk update, not a per-row loop, so it scales with membership size).
/// </summary>
public class MembershipLifecycleService(
    IApplicationDbContext db, ICacheService cache, IEmailSender emailSender,
    IDateTimeProvider dateTimeProvider, ILogger<MembershipLifecycleService> logger)
    : IMembershipLifecycleService
{
    private record ReminderCandidate(Guid Id, string Email, string FirstName, DateOnly RenewalDueDate);

    public async Task ProcessDailyAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(dateTimeProvider.UtcNow.UtcDateTime);
        var gracePeriodDays = await MembershipGracePeriod.GetDaysAsync(db, cache, cancellationToken);

        await SendRemindersAsync(today, cancellationToken);
        await FlipExpiredMembersAsync(today, gracePeriodDays, cancellationToken);
    }

    private async Task SendRemindersAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var candidates = await db.Members
            .Where(m => m.Status == MembershipStatus.Active && m.RenewalDueDate != null)
            .Select(m => new ReminderCandidate(m.Id, m.User.Email!, m.FirstName, m.RenewalDueDate!.Value))
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            var reminderType = SelectReminderType(candidate.RenewalDueDate, today);
            if (reminderType is null)
            {
                continue;
            }

            try
            {
                var alreadySent = await db.RenewalReminderLogs.AsNoTracking().AnyAsync(
                    r => r.MemberId == candidate.Id && r.ReminderType == reminderType
                        && r.ForRenewalDueDate == candidate.RenewalDueDate, cancellationToken);
                if (alreadySent)
                {
                    continue;
                }

                var (subject, body) = BuildReminderContent(reminderType.Value, candidate);
                await emailSender.SendEmailAsync(candidate.Email, subject, body, cancellationToken);

                db.RenewalReminderLogs.Add(new RenewalReminderLog
                {
                    MemberId = candidate.Id,
                    ReminderType = reminderType.Value,
                    ForRenewalDueDate = candidate.RenewalDueDate,
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One member's failed send (bad address, SMTP hiccup, or a rare unique-index
                // collision from a rolling-deploy overlap) must never block the rest of the run.
                logger.LogWarning(
                    ex, "Renewal reminder ({ReminderType}) failed for member {MemberId}", reminderType, candidate.Id);
            }
        }
    }

    /// <summary>
    /// 30/7/0 days before due, or exactly the first day past due (grace period entry) - a single
    /// reminder, not a daily repeat throughout the grace window.
    /// </summary>
    private static RenewalReminderType? SelectReminderType(DateOnly renewalDueDate, DateOnly today)
    {
        var daysUntilDue = renewalDueDate.DayNumber - today.DayNumber;
        return daysUntilDue switch
        {
            30 => RenewalReminderType.ThirtyDaysBefore,
            7 => RenewalReminderType.SevenDaysBefore,
            0 => RenewalReminderType.DueDate,
            -1 => RenewalReminderType.GracePeriod,
            _ => null,
        };
    }

    private static (string Subject, string Body) BuildReminderContent(RenewalReminderType type, ReminderCandidate candidate)
    {
        var dueDateText = candidate.RenewalDueDate.ToString("MMMM d, yyyy");
        return type switch
        {
            RenewalReminderType.ThirtyDaysBefore => (
                "Your PSMPE Membership Renewal Is Due in 30 Days",
                $"<p>Hi {candidate.FirstName},</p><p>Your PSMPE membership renewal is due on <strong>{dueDateText}</strong> - " +
                "30 days from now. You can pay your annual dues any time from your Profile page.</p>"),
            RenewalReminderType.SevenDaysBefore => (
                "Your PSMPE Membership Renewal Is Due in 7 Days",
                $"<p>Hi {candidate.FirstName},</p><p>Your PSMPE membership renewal is due on <strong>{dueDateText}</strong> - " +
                "just 7 days from now. Please pay your annual dues from your Profile page to avoid any interruption.</p>"),
            RenewalReminderType.DueDate => (
                "Your PSMPE Membership Renewal Is Due Today",
                $"<p>Hi {candidate.FirstName},</p><p>Your PSMPE membership renewal is due <strong>today</strong>. " +
                "Please pay your annual dues from your Profile page.</p>"),
            _ => (
                "Your PSMPE Membership Has Entered Its Grace Period",
                $"<p>Hi {candidate.FirstName},</p><p>Your PSMPE membership renewal was due on <strong>{dueDateText}</strong> " +
                "and has now entered its grace period. Please pay your annual dues from your Profile page soon - " +
                "once the grace period ends, portal access is restricted until you renew.</p>"),
        };
    }

    /// <summary>
    /// A single bulk statement, not a per-row loop - this must scale with membership size regardless
    /// of how many members are due to flip on a given day.
    /// </summary>
    private Task FlipExpiredMembersAsync(DateOnly today, int gracePeriodDays, CancellationToken cancellationToken)
    {
        DateTimeOffset? now = dateTimeProvider.UtcNow;
        return db.Members
            .Where(m => m.Status == MembershipStatus.Active && m.RenewalDueDate != null
                && today > m.RenewalDueDate!.Value.AddDays(gracePeriodDays))
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, MembershipStatus.Expired)
                .SetProperty(m => m.UpdatedAt, now), cancellationToken);
    }
}
