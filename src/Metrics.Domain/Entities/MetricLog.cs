namespace Metrics.Domain.Entities;

public class MetricLog
{
    public Guid Id { get; set; }
    public Guid AutomationId { get; set; }
    public Guid ReportedBy { get; set; }
    public DateTime ReportedAt { get; set; }

    public List<MetricLogValue> Values { get; set; } = [];
}

/// <summary>
/// One value in a log. Label, type, currency code and (for computed rows) the formula are copied from the
/// definition at report time, so history renders and stays unchanged even after the definition is deleted.
/// A null <see cref="Value"/> on a computed row means the result was undefined (e.g. division by zero).
/// </summary>
public class MetricLogValue
{
    public Guid Id { get; set; }
    public Guid MetricLogId { get; set; }
    public Guid MetricDefinitionId { get; set; }
    public LogValueRole Role { get; set; }
    public decimal? Value { get; set; }
    public string LabelSnapshot { get; set; } = "";
    public MetricValueType ValueTypeSnapshot { get; set; }
    public string? CurrencyCodeSnapshot { get; set; }
    public string? FormulaSnapshot { get; set; }
}
