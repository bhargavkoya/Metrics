using Metrics.Application.Caching;

namespace Metrics.Application.Logs;

/// <summary>
/// Read-through cache around <see cref="RoiService"/>. The key contains the automation's DataVersion, which every write
/// that affects ROI data (a report, or a metric added or deleted) bumps. The version is read from the database first, so
/// a stale payload can never be served as current and there is no invalidate-then-repopulate race: after a write the old
/// key simply stops being asked for and expires. The only uncached work per request is that one indexed version lookup.
/// </summary>
public class CachedRoiService(
    IRoiService inner, ILogRepository versions, IMetricCache cache, RoiSettings settings, TimeProvider clock, CacheTrace trace)
    : IRoiService
{
    public static string Key(Guid automationId, long version, int points) => $"roi:{automationId:N}:v{version}:p{points}";

    public async Task<RoiDto> GetAsync(Guid automationId, int points, CancellationToken ct)
    {
        points = Math.Clamp(points, 1, RoiService.MaxPoints);
        var version = await versions.GetDataVersionAsync(automationId, ct);

        var lookup = await cache.GetAsync<RoiDto>(Key(automationId, version, points), ct);
        RoiDto dto;

        if (lookup.Outcome == CacheOutcome.Hit)
        {
            dto = lookup.Value!;
            trace.Status = CacheTrace.Hit;
        }
        else
        {
            dto = await inner.GetAsync(automationId, points, ct);
            trace.Status = lookup.Outcome == CacheOutcome.Miss ? CacheTrace.Miss : CacheTrace.Bypass;

            // Store under the version the payload was actually built from (a write may have landed since we read it).
            if (lookup.Outcome == CacheOutcome.Miss)
                await cache.SetAsync(Key(automationId, dto.DataVersion, points), dto, TimeSpan.FromMinutes(settings.CacheTtlMinutes), ct);
        }

        // Staleness depends on the current time, so it is applied after the cache and never stored in it.
        return dto with { IsStale = StalenessPolicy.IsStale(dto.AsOf, clock.GetUtcNow().UtcDateTime, settings.StaleAfterDays) };
    }
}
