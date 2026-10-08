using Metrics.Application.Common;
using Metrics.Application.Metrics;

namespace Metrics.Application.Logs;

/// <summary>
/// Signals that an automation's ROI data changed. The in-process implementation only works on a single server;
/// scaling out would swap it for Redis pub/sub behind this same interface.
/// </summary>
public interface IChangeNotifier
{
    /// <summary>A task that completes at the next <see cref="Notify"/> for this automation.</summary>
    Task NextChangeAsync(Guid automationId);

    void Notify(Guid automationId);
}

public interface IRoiLongPoll
{
    /// <summary>
    /// Returns the ROI payload as soon as the automation's DataVersion exceeds <paramref name="sinceVersion"/>,
    /// or null if nothing changed within <paramref name="timeout"/>.
    /// </summary>
    Task<RoiDto?> WaitForChangeAsync(Guid automationId, long sinceVersion, int points, TimeSpan timeout, CancellationToken ct);
}

public class RoiLongPollService(
    IMetricRepository metrics, ILogRepository logs, IRoiService roi, IChangeNotifier notifier, TimeProvider clock) : IRoiLongPoll
{
    /// <summary>Safety net: re-check the database even without a signal (covers a change made by another server).</summary>
    public TimeSpan RecheckInterval { get; init; } = TimeSpan.FromSeconds(5);

    public async Task<RoiDto?> WaitForChangeAsync(Guid automationId, long sinceVersion, int points, TimeSpan timeout, CancellationToken ct)
    {
        if (!await metrics.AutomationExistsAsync(automationId, ct))
            throw new NotFoundException("Automation not found.");

        var deadline = clock.GetUtcNow() + timeout;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // Take the signal BEFORE checking the version. A change that commits in between completes this signal,
            // and a change that committed earlier is already visible to the check, so no change can be missed.
            var signal = notifier.NextChangeAsync(automationId);

            var version = await logs.GetDataVersionAsync(automationId, ct);
            if (version > sinceVersion) return await roi.GetAsync(automationId, points, ct);

            var remaining = deadline - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero) return null;

            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var delay = Task.Delay(remaining < RecheckInterval ? remaining : RecheckInterval, delayCts.Token);
            await Task.WhenAny(signal, delay);
            delayCts.Cancel(); // don't leave the timer running when the signal won
        }
    }
}
