namespace Metrics.Domain.Entities;

public class MetricLog
{
    public Guid Id { get; set; }
    public Guid AutomationId { get; set; }
    public Guid ReportedBy { get; set; }
    public DateTime ReportedAt { get; set; }

    public List<MetricLogValue> Values { get; set; } = [];
}

/// <summary>Label and type are snapshotted so history renders even after a definition is deleted.</summary>
public class MetricLogValue
{
    public Guid Id { get; set; }
    public Guid MetricLogId { get; set; }
    public Guid MetricDefinitionId { get; set; }
    public LogValueRole Role { get; set; }
    public decimal? Value { get; set; }
    public string LabelSnapshot { get; set; } = "";
    public MetricValueType ValueTypeSnapshot { get; set; }
}
