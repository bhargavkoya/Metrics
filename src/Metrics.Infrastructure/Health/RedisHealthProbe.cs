using Metrics.Application.Abstractions;
using Microsoft.Extensions.Caching.Distributed;

namespace Metrics.Infrastructure.Health;

public class RedisHealthProbe(IDistributedCache cache) : IHealthProbe
{
    public string Name => "redis";

    public async Task<HealthProbeResult> CheckAsync(CancellationToken ct)
    {
        try
        {
            await cache.SetStringAsync("health:ping", "pong",
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10) }, ct);
            var v = await cache.GetStringAsync("health:ping", ct);
            return new(Name, v == "pong");
        }
        catch (Exception ex)
        {
            return new(Name, false, ex.Message);
        }
    }
}
