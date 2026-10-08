using Metrics.Application.Common;
using Metrics.Application.Metrics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Metrics.Api.Controllers;

/// <summary>
/// Metric definitions per automation. Reads are open to any employee; writes are Technical-only on any automation.
/// There is deliberately no update route: formulas (and labels) are immutable. Delete and recreate instead.
/// </summary>
[ApiController]
[Route("api/automations/{automationId:guid}/metrics")]
public class MetricsController(IMetricService metrics) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MetricDefinitionDto>>> List(Guid automationId, CancellationToken ct) =>
        Ok(await metrics.ListAsync(automationId, ct));

    [Authorize(Policy = Policies.Technical)]
    [HttpPost]
    public async Task<ActionResult<MetricDefinitionDto>> Create(
        Guid automationId, CreateMetricRequest request, CancellationToken ct)
    {
        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var id)
            ? id
            : throw new UnauthorizedException("Invalid token.");

        var dto = await metrics.CreateAsync(automationId, userId, request, ct);
        return Created($"/api/automations/{automationId}/metrics", dto);
    }

    /// <summary>Dry run: always 200; the body says whether the formula is valid and why not.</summary>
    [Authorize(Policy = Policies.Technical)]
    [HttpPost("validate-formula")]
    public async Task<ActionResult<ValidateFormulaResponse>> ValidateFormula(
        Guid automationId, ValidateFormulaRequest request, CancellationToken ct) =>
        Ok(await metrics.ValidateFormulaAsync(automationId, request, ct));

    [Authorize(Policy = Policies.Technical)]
    [HttpDelete("{metricId:guid}")]
    public async Task<IActionResult> Delete(Guid automationId, Guid metricId, CancellationToken ct)
    {
        await metrics.DeleteAsync(automationId, metricId, ct);
        return NoContent();
    }
}
