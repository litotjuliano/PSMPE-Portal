namespace PSMPE.Portal.Application.Common.Interfaces;

/// <summary>
/// Daily membership housekeeping: sends renewal reminder emails at fixed points before/at/after
/// RenewalDueDate, and auto-flips a lapsed member's Status once the grace period ends. See
/// MembershipLifecycleService (Infrastructure) and MembershipLifecycleBackgroundService, the
/// PeriodicTimer wrapper that calls this once a day - same shape as ILogRetentionService.
/// </summary>
public interface IMembershipLifecycleService
{
    Task ProcessDailyAsync(CancellationToken cancellationToken = default);
}
