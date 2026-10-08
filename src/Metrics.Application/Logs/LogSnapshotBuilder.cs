using Metrics.Application.Formulas;
using Metrics.Domain;
using Metrics.Domain.Entities;

namespace Metrics.Application.Logs;

/// <summary>
/// Builds a log with its full snapshot: one row per input value and one per live computed metric, each carrying the
/// label, type, currency and formula as they are right now. Used for real reports and for seed data, so both follow
/// exactly the same rules. After this runs nothing ever recomputes the row: history is what was recorded.
/// </summary>
public static class LogSnapshotBuilder
{
    public static MetricLog Build(
        Guid automationId,
        Guid reportedBy,
        DateTime reportedAt,
        IReadOnlyList<MetricDefinition> liveDefinitions,
        IReadOnlyDictionary<Guid, decimal> inputValues,
        IFormulaEngine engine)
    {
        var inputs = liveDefinitions.Where(d => d.Kind == MetricKind.Input).ToList();
        var types = inputs.ToDictionary(d => d.Label, d => new MetricType(d.ValueType, d.CurrencyCode), StringComparer.OrdinalIgnoreCase);
        var byLabel = inputs.ToDictionary(d => d.Label, d => inputValues[d.Id], StringComparer.OrdinalIgnoreCase);

        var log = new MetricLog { Id = Guid.NewGuid(), AutomationId = automationId, ReportedBy = reportedBy, ReportedAt = reportedAt };

        foreach (var d in inputs)
            log.Values.Add(Row(log, d, LogValueRole.Input, inputValues[d.Id], null));

        foreach (var d in liveDefinitions.Where(d => d.Kind == MetricKind.Computed))
        {
            // Null when the data makes the result undefined (e.g. a zero divisor); the report itself still succeeds.
            var value = engine.Evaluate(d.FormulaText!, byLabel, types);
            log.Values.Add(Row(log, d, LogValueRole.Computed, value, d.FormulaText));
        }

        return log;
    }

    private static MetricLogValue Row(MetricLog log, MetricDefinition d, LogValueRole role, decimal? value, string? formula) => new()
    {
        Id = Guid.NewGuid(),
        MetricLogId = log.Id,
        MetricDefinitionId = d.Id,
        Role = role,
        Value = value,
        LabelSnapshot = d.Label,
        ValueTypeSnapshot = d.ValueType,
        CurrencyCodeSnapshot = d.CurrencyCode,
        FormulaSnapshot = formula
    };
}
