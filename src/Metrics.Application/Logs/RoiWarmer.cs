using Metrics.Application.Caching;

namespace Metrics.Application.Logs;

/// <summary>The set of automations whose reporting is overdue, as last computed by the background worker.</summary>
public interface IStaleFlagStore
{
    Task SetAsync(IReadOnlyCollection<Guid> automationIds, TimeSpan ttl, CancellationToken ct);

    /// <summary>Empty when the flags have expired or the cache is unavailable: no flag is better than a wrong one.</summary>
    Task<IReadOnlySet<Guid>> GetAsync(CancellationToken ct);
}

public class CacheStaleFlagStore(IMetricCache cache) : IStaleFlagStore
{
    private const string Key = "roi:stale";

    private sealed record Flags(List<Guid> Ids);

    public Task SetAsync(IReadOnlyCollection<Guid> automationIds, TimeSpan ttl, CancellationToken ct) =>
        cache.SetAsync(Key, new Flags(automationIds.ToList()), ttl, ct);

    public async Task<IReadOnlySet<Guid>> GetAsync(CancellationToken ct)
    {
        var lookup = await cache.GetAsync<Flags>(Key, ct);
        return lookup.Outcome == CacheOutcome.Hit ? lookup.Value!.Ids.ToHashSet() : new HashSet<Guid>();
    }
}

public sealed record WarmResult(int Warmed, IReadOnlyList<Guid> Stale, IReadOnlyList<(Guid AutomationId, Exception Error)> Failures);

/// <summary>
/// One cycle of the background worker: warm the ROI cache for every automation that has reports, and record which of them
/// are stale. Warming is just a read through the cached service, so it fills the cache when the current version is missing
/// and is a plain hit otherwise.
/// </summary>
public class RoiWarmer(ILogRepository logs, IRoiService roi, IStaleFlagStore flags, RoiSettings settings)
{
    public async Task<WarmResult> RunOnceAsync(CancellationToken ct)
    {
        var warmed = 0;
        var stale = new List<Guid>();
        var failures = new List<(Guid, Exception)>();

        foreach (var id in await logs.GetAutomationIdsWithLogsAsync(ct))
        {
            try
            {
                var dto = await roi.GetAsync(id, RoiService.DefaultPoints, ct);
                warmed++;
                if (dto.IsStale) stale.Add(id);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add((id, ex)); // one bad automation must not stop the rest
            }
        }

        // Flags expire after a few missed cycles, so a dead worker stops flagging instead of showing outdated flags.
        await flags.SetAsync(stale, TimeSpan.FromSeconds(settings.WarmIntervalSeconds * 3), ct);
        return new WarmResult(warmed, stale, failures);
    }
}
