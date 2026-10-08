namespace Metrics.Domain.Entities;

public class MetricDefinition
{
    public Guid Id { get; set; }
    public Guid AutomationId { get; set; }
    public string Label { get; set; } = "";
    public MetricValueType ValueType { get; set; }
    public MetricKind Kind { get; set; }

    /// <summary>Immutable once created. Null for input metrics.</summary>
    public string? FormulaText { get; set; }

    public string? CurrencyCode { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
