namespace Metrics.Application.Caching;

public enum CacheOutcome
{
    Hit,
    Miss,

    /// <summary>The cache could not be reached (or is being skipped after a recent failure). Callers fall back to the source.</summary>
    Unavailable
}

public readonly record struct CacheLookup<T>(CacheOutcome Outcome, T? Value) where T : class;

/// <summary>
/// Cache access that never throws for infrastructure problems: a failing cache degrades to <see cref="CacheOutcome.Unavailable"/>
/// so reads fall back to the database instead of failing the request.
/// </summary>
public interface IMetricCache
{
    Task<CacheLookup<T>> GetAsync<T>(string key, CancellationToken ct) where T : class;

    /// <returns>False when the value was not stored (cache unavailable).</returns>
    Task<bool> SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct) where T : class;

    Task RemoveAsync(string key, CancellationToken ct);
}

/// <summary>Per-request record of how the ROI read was served, surfaced as the X-Cache response header.</summary>
public sealed class CacheTrace
{
    public const string Hit = "HIT";
    public const string Miss = "MISS";
    public const string Bypass = "BYPASS";

    public string? Status { get; set; }
}

/// <summary>The "Roi" configuration section. None of these are secrets.</summary>
public sealed class RoiSettings
{
    public const string SectionName = "Roi";

    public int CacheTtlMinutes { get; set; } = 10;
    public bool WarmerEnabled { get; set; } = true;
    public int WarmIntervalSeconds { get; set; } = 60;
    public int StaleAfterDays { get; set; } = 14;
    public int LongPollMaxSeconds { get; set; } = 25;
}

/// <summary>"Stale" means reporting is overdue: the latest report is older than the threshold. Never-reported is not stale.</summary>
public static class StalenessPolicy
{
    public static bool IsStale(DateTime? lastReportedAt, DateTime now, int staleAfterDays) =>
        lastReportedAt is { } last && now - last > TimeSpan.FromDays(staleAfterDays);
}
