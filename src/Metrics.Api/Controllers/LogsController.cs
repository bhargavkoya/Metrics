using Metrics.Application.Caching;
using Metrics.Application.Common;
using Metrics.Application.Logs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Metrics.Api.Controllers;

/// <summary>Reading is open to any employee; reporting is Technical-only on any automation (global access).</summary>
[ApiController]
[Route("api/automations/{automationId:guid}")]
public class LogsController(ILogService logs, IRoiService roi, IRoiLongPoll longPoll, CacheTrace cacheTrace, RoiSettings roiSettings)
    : ControllerBase
{
    /// <summary>Longest a long-poll request may be held open, regardless of the configured default.</summary>
    private const int HardMaxLongPollSeconds = 30;

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

    /// <summary>Current figures plus chart series in one call. <c>X-Cache</c> says whether Redis served it.</summary>
    [HttpGet("roi")]
    public async Task<ActionResult<RoiDto>> Roi(
        Guid automationId, [FromQuery] int points = RoiService.DefaultPoints, CancellationToken ct = default)
    {
        var dto = await roi.GetAsync(automationId, points, ct);
        AddCacheHeader();
        return Ok(dto);
    }

    /// <summary>
    /// Long poll: held open until the automation's ROI data moves past <paramref name="sinceVersion"/>, then answers with
    /// the new payload. Answers 204 if nothing changed within the timeout; the client simply asks again.
    /// </summary>
    [HttpGet("roi/changes")]
    public async Task<ActionResult<RoiDto>> RoiChanges(
        Guid automationId,
        [FromQuery] long sinceVersion,
        [FromQuery] int timeoutSeconds = 25,
        [FromQuery] int points = RoiService.DefaultPoints,
        CancellationToken ct = default)
    {
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, Math.Min(roiSettings.LongPollMaxSeconds, HardMaxLongPollSeconds)));
        try
        {
            var dto = await longPoll.WaitForChangeAsync(automationId, sinceVersion, points, timeout, ct);
            if (dto is null) return NoContent();
            AddCacheHeader();
            return Ok(dto);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return NoContent(); // the client went away; nobody is listening for this response
        }
    }

    private void AddCacheHeader()
    {
        if (cacheTrace.Status is { } status) Response.Headers["X-Cache"] = status;
    }
}
