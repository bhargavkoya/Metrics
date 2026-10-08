using Metrics.Application.Abstractions;
using Metrics.Infrastructure.Persistence;

namespace Metrics.Infrastructure.Health;

public class DbHealthProbe(MetricsDbContext db) : IHealthProbe
{
    public string Name => "postgres";

    public async Task<HealthProbeResult> CheckAsync(CancellationToken ct)
    {
        try
        {
            return new(Name, await db.Database.CanConnectAsync(ct));
        }
        catch (Exception ex)
        {
            return new(Name, false, ex.Message);
        }
    }
}
