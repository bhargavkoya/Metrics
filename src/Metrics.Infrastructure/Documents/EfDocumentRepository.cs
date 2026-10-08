using Metrics.Application.Documents;
using Metrics.Domain.Entities;
using Metrics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Metrics.Infrastructure.Documents;

public class EfDocumentRepository(MetricsDbContext db) : IDocumentRepository
{
    public Task<bool> AutomationExistsAsync(Guid automationId, CancellationToken ct) =>
        db.Automations.AnyAsync(a => a.Id == automationId, ct);

    public Task<List<DocumentDto>> ListAsync(Guid automationId, CancellationToken ct) =>
        Project(db.Documents.AsNoTracking().Where(d => d.AutomationId == automationId)).ToListAsync(ct);

    public Task<DocumentDto?> GetDtoAsync(Guid documentId, CancellationToken ct) =>
        Project(db.Documents.AsNoTracking().Where(d => d.Id == documentId)).FirstOrDefaultAsync(ct);

    public Task<AutomationDocument?> FindAsync(Guid automationId, Guid documentId, CancellationToken ct) =>
        db.Documents.FirstOrDefaultAsync(d => d.Id == documentId && d.AutomationId == automationId, ct);

    public async Task AddAsync(AutomationDocument doc, DateTime activityAt, CancellationToken ct)
    {
        db.Documents.Add(doc);
        await TouchAsync(doc.AutomationId, activityAt, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(AutomationDocument doc, DateTime activityAt, CancellationToken ct)
    {
        db.Documents.Remove(doc);
        await TouchAsync(doc.AutomationId, activityAt, ct);
        await db.SaveChangesAsync(ct);
    }

    // Left join: a deleted uploader shows as "Unknown" instead of hiding the document.
    // Newest first. The ordering must sit before the select: EF can't translate OrderBy over a constructed DTO.
    private IQueryable<DocumentDto> Project(IQueryable<AutomationDocument> docs) =>
        from d in docs
        join u in db.Users on d.UploadedBy equals u.Id into users
        from u in users.DefaultIfEmpty()
        orderby d.UploadedAt descending
        select new DocumentDto(d.Id, d.OriginalFileName, d.ContentType, d.SizeBytes, u == null ? "Unknown" : u.Name, d.UploadedAt);

    private async Task TouchAsync(Guid automationId, DateTime activityAt, CancellationToken ct)
    {
        var automation = await db.Automations.FirstAsync(a => a.Id == automationId, ct);
        automation.LastActivityAt = activityAt;
    }
}
