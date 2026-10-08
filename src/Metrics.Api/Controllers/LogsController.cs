using Metrics.Application.Common;
using Metrics.Application.Logs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Metrics.Api.Controllers;

/// <summary>Reading is open to any employee; reporting is Technical-only on any automation (global access).</summary>
[ApiController]
[Route("api/automations/{automationId:guid}")]
public class LogsController(ILogService logs, IRoiService roi) : ControllerBase
{
    [Authorize(Policy = Policies.Technical)]
    [HttpPost("logs")]
    public async Task<ActionResult<LogDto>> Report(Guid automationId, ReportLogRequest request, CancellationToken ct)
    {
        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var id)
            ? id
            : throw new UnauthorizedException("Invalid token.");

        var dto = await logs.ReportAsync(automationId, userId, request, ct);
        return Created($"/api/automations/{automationId}/logs", dto);
    }

    [HttpGet("logs")]
    public async Task<ActionResult<PagedResult<LogDto>>> List(
        Guid automationId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await logs.ListAsync(automationId, page, pageSize, ct));

    /// <summary>Current figures plus chart series in one call.</summary>
    [HttpGet("roi")]
    public async Task<ActionResult<RoiDto>> Roi(
        Guid automationId, [FromQuery] int points = RoiService.DefaultPoints, CancellationToken ct = default) =>
        Ok(await roi.GetAsync(automationId, points, ct));
}
