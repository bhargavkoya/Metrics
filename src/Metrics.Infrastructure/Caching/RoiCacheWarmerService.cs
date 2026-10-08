using Metrics.Application.Caching;
using Metrics.Application.Logs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Metrics.Infrastructure.Caching;

/// <summary>
/// Periodically warms the ROI cache and refreshes the stale-automation flags. The work itself lives in
/// <see cref="RoiWarmer"/>; this class only schedules it and keeps one bad cycle from killing the loop.
/// </summary>
public sealed class RoiCacheWarmerService(IServiceScopeFactory scopes, RoiSettings settings, ILogger<RoiCacheWarmerService> log) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try
        {
            await Task.Delay(StartupDelay, stop);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, settings.WarmIntervalSeconds)));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<RoiWarmer>().RunOnceAsync(stop);

                log.LogInformation("ROI cache warmed for {Warmed} automation(s); {Stale} stale; {Failed} failed",
                    result.Warmed, result.Stale.Count, result.Failures.Count);
                foreach (var (id, error) in result.Failures)
                    log.LogWarning(error, "Could not warm ROI cache for automation {AutomationId}", id);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "ROI cache warming cycle failed; will retry next interval");
            }
        } while (await NextTickAsync(timer, stop));
    }

    private static async Task<bool> NextTickAsync(PeriodicTimer timer, CancellationToken stop)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stop);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
