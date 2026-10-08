using Metrics.Application.Caching;

namespace Metrics.Tests.Caching;

/// <summary>In-memory IMetricCache that records writes and can simulate an outage.</summary>
public sealed class FakeCache : IMetricCache
{
    public Dictionary<string, object> Store { get; } = new();
    public List<(string Key, TimeSpan Ttl)> Sets { get; } = [];
    public int Gets { get; private set; }
    public bool Down { get; set; }

    public Task<CacheLookup<T>> GetAsync<T>(string key, CancellationToken ct) where T : class
    {
        Gets++;
        if (Down) return Task.FromResult(new CacheLookup<T>(CacheOutcome.Unavailable, null));
        return Task.FromResult(Store.TryGetValue(key, out var v)
            ? new CacheLookup<T>(CacheOutcome.Hit, (T)v)
            : new CacheLookup<T>(CacheOutcome.Miss, null));
    }

    public Task<bool> SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct) where T : class
    {
        if (Down) return Task.FromResult(false);
        Store[key] = value;
        Sets.Add((key, ttl));
        return Task.FromResult(true);
    }

    public Task RemoveAsync(string key, CancellationToken ct)
    {
        Store.Remove(key);
        return Task.CompletedTask;
    }
}

/// <summary>A clock tests can move forward.</summary>
public sealed class AdjustableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}
