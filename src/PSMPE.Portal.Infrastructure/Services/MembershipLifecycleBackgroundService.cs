using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.Infrastructure.Services;

/// <summary>
/// Second scheduled job in this codebase, same shape as LogRetentionBackgroundService: a plain
/// daily PeriodicTimer rather than a scheduling library. Runs once immediately on startup (the
/// do/while below checks its condition after the body runs), then once every 24h after that, so a
/// restart-heavy deployment doesn't wait a full day for its first reminder pass. Each tick opens
/// its own DI scope, since IApplicationDbContext is scoped and this service itself is a singleton
/// for the app's lifetime.
/// </summary>
public class MembershipLifecycleBackgroundService(
    IServiceScopeFactory scopeFactory, ILogger<MembershipLifecycleBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var lifecycleService = scope.ServiceProvider.GetRequiredService<IMembershipLifecycleService>();
                await lifecycleService.ProcessDailyAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Membership lifecycle processing failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
