using System.Text.Json;
using System.Text.Json.Serialization;
using Metrics.Application.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Metrics.Infrastructure.Caching;

/// <summary>
/// JSON cache over IDistributedCache (Redis in production). Infrastructure failures never reach callers: they get
/// <see cref="CacheOutcome.Unavailable"/> and fall back to the database. After a failure the cache is skipped for a
/// short while so each request doesn't wait out the Redis connect timeout.
/// </summary>
public sealed class DistributedMetricCache(IDistributedCache cache, TimeProvider clock, ILogger<DistributedMetricCache> log) : IMetricCache
{
    public static readonly TimeSpan BackoffAfterFailure = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private long _unavailableUntilTicks;

    private bool InBackoff => clock.GetUtcNow().UtcTicks < Interlocked.Read(ref _unavailableUntilTicks);

    public async Task<CacheLookup<T>> GetAsync<T>(string key, CancellationToken ct) where T : class
    {
        if (InBackoff) return new(CacheOutcome.Unavailable, null);

        byte[]? bytes;
        try
        {
            bytes = await cache.GetAsync(key, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trip(ex);
            return new(CacheOutcome.Unavailable, null);
        }

        if (bytes is null) return new(CacheOutcome.Miss, null);

        try
        {
            var value = JsonSerializer.Deserialize<T>(bytes, Json);
            return value is null ? new(CacheOutcome.Miss, null) : new(CacheOutcome.Hit, value);
        }
        catch (JsonException ex)
        {
            // An entry written in an older shape: treat it as a miss and let the caller overwrite it.
            log.LogWarning(ex, "Ignoring unreadable cache entry {Key}", key);
            return new(CacheOutcome.Miss, null);
        }
    }

    public async Task<bool> SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct) where T : class
    {
        if (InBackoff) return false;
        try
        {
            await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, Json),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trip(ex);
            return false;
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct)
    {
        if (InBackoff) return;
        try
        {
            await cache.RemoveAsync(key, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trip(ex);
        }
    }

    private void Trip(Exception ex)
    {
        log.LogWarning(ex, "Cache unavailable; bypassing it for {Seconds}s", BackoffAfterFailure.TotalSeconds);
        Interlocked.Exchange(ref _unavailableUntilTicks, clock.GetUtcNow().Add(BackoffAfterFailure).UtcTicks);
    }
}
