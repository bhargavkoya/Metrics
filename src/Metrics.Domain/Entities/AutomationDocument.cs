namespace Metrics.Domain.Entities;

public class AutomationDocument
{
    public Guid Id { get; set; }
    public Guid AutomationId { get; set; }
    public string OriginalFileName { get; set; } = "";

    /// <summary>Generated GUID filename on disk; never derived from client input.</summary>
    public string StoredFileName { get; set; } = "";

    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public Guid UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; }
}
