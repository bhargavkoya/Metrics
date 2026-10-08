using Metrics.Application.Automations;
using Microsoft.AspNetCore.Mvc;

namespace Metrics.Api.Controllers;

/// <summary>Read-only for every authenticated employee; access is global (no per-automation ownership).</summary>
[ApiController]
[Route("api/automations")]
public class AutomationsController(IAutomationService automations) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AutomationCardDto>>> List(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? department, [FromQuery] string? q, CancellationToken ct) =>
        Ok(await automations.ListAsync(new AutomationFilter(from, to, department, q), ct));

    [HttpGet("departments")]
    public async Task<ActionResult<IReadOnlyList<string>>> Departments(CancellationToken ct) =>
        Ok(await automations.DepartmentsAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AutomationDetailDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await automations.GetAsync(id, ct));
}
