namespace Metrics.Domain.Entities;

public class Automation
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Client { get; set; } = "";
    public string Requirement { get; set; } = "";
    public string Department { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }

    /// <summary>Bumped on every write that affects ROI data; drives cache keys and long polling.</summary>
    public long DataVersion { get; set; }

    public List<AutomationDocument> Documents { get; set; } = [];
    public List<MetricDefinition> Metrics { get; set; } = [];
    public List<MetricLog> Logs { get; set; } = [];
}
