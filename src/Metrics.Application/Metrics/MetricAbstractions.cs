using Metrics.Application.Formulas;
using Metrics.Domain;
using Metrics.Domain.Entities;

namespace Metrics.Application.Metrics;

/// <param name="ValueType">Required for inputs; ignored for computed metrics (inferred from the formula).</param>
/// <param name="CurrencyCode">Inputs of type Currency only; defaults to USD.</param>
/// <param name="Formula">Computed metrics only.</param>
public record CreateMetricRequest(string Label, MetricKind Kind, MetricValueType? ValueType, string? CurrencyCode, string? Formula);

public record ValidateFormulaRequest(string? Formula);

public record FormulaErrorDto(string Code, string Message, int Position);

public record ValidateFormulaResponse(
    bool Valid,
    MetricValueType? ResultType,
    string? CurrencyCode,
    IReadOnlyList<string> References,
    IReadOnlyList<FormulaErrorDto> Errors);

public record MetricDefinitionDto(
    Guid Id, string Label, MetricKind Kind, MetricValueType ValueType,
    string? CurrencyCode, string? FormulaText, DateTime CreatedAt);

public interface IMetricRepository
{
    Task<bool> AutomationExistsAsync(Guid automationId, CancellationToken ct);

    /// <summary>Live (not deleted) definitions, oldest first.</summary>
    Task<List<MetricDefinition>> ListLiveAsync(Guid automationId, CancellationToken ct);

    /// <summary>A live definition, or null when it does not exist, was deleted, or belongs to another automation.</summary>
    Task<MetricDefinition?> FindLiveAsync(Guid automationId, Guid metricId, CancellationToken ct);

    /// <summary>Adds the definition and bumps the automation's DataVersion in one save.</summary>
    Task AddAsync(MetricDefinition definition, CancellationToken ct);

    /// <summary>Soft-deletes the definition and bumps the automation's DataVersion in one save.</summary>
    Task SoftDeleteAsync(MetricDefinition definition, DateTime deletedAt, CancellationToken ct);
}

public interface IMetricService
{
    Task<IReadOnlyList<MetricDefinitionDto>> ListAsync(Guid automationId, CancellationToken ct);
    Task<MetricDefinitionDto> CreateAsync(Guid automationId, Guid userId, CreateMetricRequest request, CancellationToken ct);
    Task<ValidateFormulaResponse> ValidateFormulaAsync(Guid automationId, ValidateFormulaRequest request, CancellationToken ct);
    Task DeleteAsync(Guid automationId, Guid metricId, CancellationToken ct);
}
