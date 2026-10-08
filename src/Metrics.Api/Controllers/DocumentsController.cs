using Metrics.Application.Common;
using Metrics.Application.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Metrics.Api.Controllers;

/// <summary>Read/download for any employee; upload/delete for the Technical team on any automation (global access).</summary>
[ApiController]
[Route("api/automations/{automationId:guid}/documents")]
public class DocumentsController(IDocumentService documents) : ControllerBase
{
    // Slightly above the 10 MB cap so oversize files get a clear 400 from the service; far larger bodies get 413.
    private const long RequestLimit = 12 * 1024 * 1024;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DocumentDto>>> List(Guid automationId, CancellationToken ct) =>
        Ok(await documents.ListAsync(automationId, ct));

    [Authorize(Policy = Policies.Technical)]
    [HttpPost]
    [RequestSizeLimit(RequestLimit)]
    public async Task<ActionResult<DocumentDto>> Upload(Guid automationId, IFormFile file, CancellationToken ct)
    {
        var uploaderId = Guid.TryParse(User.FindFirst("sub")?.Value, out var id)
            ? id
            : throw new UnauthorizedException("Invalid token.");

        await using var stream = file.OpenReadStream();
        var dto = await documents.UploadAsync(automationId, uploaderId, file.FileName, stream, ct);
        return Created($"/api/automations/{automationId}/documents/{dto.Id}/download", dto);
    }

    [HttpGet("{documentId:guid}/download")]
    public async Task<IActionResult> Download(Guid automationId, Guid documentId, CancellationToken ct)
    {
        var file = await documents.OpenAsync(automationId, documentId, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, file.ContentType, file.FileName);
    }

    [Authorize(Policy = Policies.Technical)]
    [HttpDelete("{documentId:guid}")]
    public async Task<IActionResult> Delete(Guid automationId, Guid documentId, CancellationToken ct)
    {
        await documents.DeleteAsync(automationId, documentId, ct);
        return NoContent();
    }
}
