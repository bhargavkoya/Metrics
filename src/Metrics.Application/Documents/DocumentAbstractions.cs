using Metrics.Domain.Entities;

namespace Metrics.Application.Documents;

public record DocumentDto(Guid Id, string OriginalFileName, string ContentType, long SizeBytes, string UploadedBy, DateTime UploadedAt);

public sealed record DocumentDownload(Stream Content, string FileName, string ContentType);

/// <summary>Blob storage keyed by a server-generated stored name; callers never supply paths.</summary>
public interface IFileStorage
{
    Task SaveAsync(string storedName, Stream content, CancellationToken ct);

    /// <summary>Returns null when the blob no longer exists.</summary>
    Task<Stream?> OpenReadAsync(string storedName, CancellationToken ct);

    /// <summary>No-op when the blob does not exist.</summary>
    Task DeleteAsync(string storedName, CancellationToken ct);
}

public interface IDocumentRepository
{
    Task<bool> AutomationExistsAsync(Guid automationId, CancellationToken ct);
    Task<List<DocumentDto>> ListAsync(Guid automationId, CancellationToken ct);
    Task<DocumentDto?> GetDtoAsync(Guid documentId, CancellationToken ct);
    Task<AutomationDocument?> FindAsync(Guid automationId, Guid documentId, CancellationToken ct);

    /// <summary>Adds the row and bumps the automation's LastActivityAt in one save.</summary>
    Task AddAsync(AutomationDocument doc, DateTime activityAt, CancellationToken ct);

    /// <summary>Removes the row and bumps the automation's LastActivityAt in one save.</summary>
    Task RemoveAsync(AutomationDocument doc, DateTime activityAt, CancellationToken ct);
}

public interface IDocumentService
{
    Task<IReadOnlyList<DocumentDto>> ListAsync(Guid automationId, CancellationToken ct);
    Task<DocumentDto> UploadAsync(Guid automationId, Guid uploaderId, string? fileName, Stream content, CancellationToken ct);
    Task<DocumentDownload> OpenAsync(Guid automationId, Guid documentId, CancellationToken ct);
    Task DeleteAsync(Guid automationId, Guid documentId, CancellationToken ct);
}
