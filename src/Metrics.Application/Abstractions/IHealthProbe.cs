namespace Metrics.Application.Abstractions;

public record HealthProbeResult(string Name, bool Healthy, string? Error = null);

public interface IHealthProbe
{
    string Name { get; }
    Task<HealthProbeResult> CheckAsync(CancellationToken ct);
}
