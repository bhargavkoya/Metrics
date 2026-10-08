using Metrics.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Metrics.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController(IEnumerable<IHealthProbe> probes) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var results = new List<HealthProbeResult>();
        foreach (var p in probes) results.Add(await p.CheckAsync(ct));

        var healthy = results.All(r => r.Healthy);
        var body = new
        {
            status = healthy ? "Healthy" : "Unhealthy",
            checks = results.Select(r => new { name = r.Name, healthy = r.Healthy, error = r.Error })
        };
        return healthy ? Ok(body) : StatusCode(StatusCodes.Status503ServiceUnavailable, body);
    }
}
