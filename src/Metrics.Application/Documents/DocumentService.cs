using Metrics.Application.Common;
using Metrics.Domain.Entities;

namespace Metrics.Application.Documents;

public class DocumentService(IDocumentRepository repo, IFileStorage storage, TimeProvider clock) : IDocumentService
{
    public async Task<IReadOnlyList<DocumentDto>> ListAsync(Guid automationId, CancellationToken ct)
    {
        await EnsureAutomationAsync(automationId, ct);
        return await repo.ListAsync(automationId, ct);
    }

    public async Task<DocumentDto> UploadAsync(
        Guid automationId, Guid uploaderId, string? fileName, Stream content, CancellationToken ct)
    {
        await EnsureAutomationAsync(automationId, ct);

        var (displayName, ext) = FileValidator.ValidateName(fileName);
        var buffer = await ReadBoundedAsync(content, ct);
        FileValidator.ValidateSize(buffer.Length);
        FileValidator.ValidateSignature(ext, buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, FileValidator.HeaderLength)));

        var doc = new AutomationDocument
        {
            Id = Guid.NewGuid(),
            AutomationId = automationId,
            OriginalFileName = displayName,
            StoredFileName = $"{Guid.NewGuid():N}{ext}",
            ContentType = FileValidator.ContentTypeFor(ext),
            SizeBytes = buffer.Length,
            UploadedBy = uploaderId,
            UploadedAt = clock.GetUtcNow().UtcDateTime
        };

        buffer.Position = 0;
        await storage.SaveAsync(doc.StoredFileName, buffer, ct);
        try
        {
            await repo.AddAsync(doc, doc.UploadedAt, ct);
        }
        catch
        {
            // Don't leave an orphan blob behind when the metadata row could not be written.
            await storage.DeleteAsync(doc.StoredFileName, CancellationToken.None);
            throw;
        }

        return await repo.GetDtoAsync(doc.Id, ct) ?? throw new InvalidOperationException("Document vanished after insert.");
    }

    public async Task<DocumentDownload> OpenAsync(Guid automationId, Guid documentId, CancellationToken ct)
    {
        var doc = await FindAsync(automationId, documentId, ct);
        var stream = await storage.OpenReadAsync(doc.StoredFileName, ct)
                     ?? throw new NotFoundException("The file is no longer available.");
        return new DocumentDownload(stream, doc.OriginalFileName, doc.ContentType);
    }

    public async Task DeleteAsync(Guid automationId, Guid documentId, CancellationToken ct)
    {
        var doc = await FindAsync(automationId, documentId, ct);
        await repo.RemoveAsync(doc, clock.GetUtcNow().UtcDateTime, ct);
        await storage.DeleteAsync(doc.StoredFileName, ct);
    }

    private async Task EnsureAutomationAsync(Guid automationId, CancellationToken ct)
    {
        if (!await repo.AutomationExistsAsync(automationId, ct))
            throw new NotFoundException("Automation not found.");
    }

    // The document must belong to the automation in the URL, so one automation's files can't be reached via another.
    private async Task<AutomationDocument> FindAsync(Guid automationId, Guid documentId, CancellationToken ct) =>
        await repo.FindAsync(automationId, documentId, ct) ?? throw new NotFoundException("Document not found.");

    /// <summary>Reads at most MaxBytes + 1 so an oversize upload is detected without trusting Content-Length.</summary>
    private static async Task<MemoryStream> ReadBoundedAsync(Stream source, CancellationToken ct)
    {
        var ms = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            ms.Write(chunk, 0, read);
            if (ms.Length > FileValidator.MaxBytes) break;
        }
        return ms;
    }
}
