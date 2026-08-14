using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PSMPE.Portal.Application.Common.Interfaces;

namespace PSMPE.Portal.Infrastructure.Services;

/// <summary>
/// The first scheduled job in this codebase - a plain daily PeriodicTimer rather than a
/// scheduling library, since there's exactly one job at exactly one interval. Runs once
/// immediately on startup (the do/while below checks its condition after the body runs), then
/// once every 24h after that, so a restart-heavy deployment doesn't wait a full day for its
/// first prune. Each tick opens its own DI scope, since IApplicationDbContext is scoped and this
/// service itself is a singleton for the app's lifetime.
/// </summary>
public class LogRetentionBackgroundService(
    IServiceScopeFactory scopeFactory, ILogger<LogRetentionBackgroundService> logger) : BackgroundService
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
                var retentionService = scope.ServiceProvider.GetRequiredService<ILogRetentionService>();
                await retentionService.PruneAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Log retention pruning failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
