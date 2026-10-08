using Metrics.Application.Common;
using Metrics.Application.Formulas;
using Metrics.Application.Metrics;
using Metrics.Domain;

namespace Metrics.Application.Logs;

public class LogService(IMetricRepository metrics, ILogRepository logs, IFormulaEngine engine, TimeProvider clock) : ILogService
{
    public const int MaxPageSize = 100;

    // NUMERIC(28,10): at most 10 decimal places and 18 integer digits.
    private const int MaxScale = 10;
    private const decimal MaxMagnitude = 1_000_000_000_000_000_000m / 10; // 10^17, leaves headroom under 10^18

    public async Task<LogDto> ReportAsync(Guid automationId, Guid userId, ReportLogRequest request, CancellationToken ct)
    {
        await EnsureAutomationAsync(automationId, ct);

        var live = await metrics.ListLiveAsync(automationId, ct);
        var inputs = live.Where(d => d.Kind == MetricKind.Input).ToList();
        if (inputs.Count == 0)
            throw new ValidationFailedException("values", "Define at least one input metric before reporting values.");

        var values = Validate(request, inputs);

        var reportedAt = clock.GetUtcNow().UtcDateTime;
        var log = LogSnapshotBuilder.Build(automationId, userId, reportedAt, live, values, engine);
        await logs.AddAsync(log, reportedAt, ct);

        var saved = await logs.GetAsync(log.Id, ct) ?? throw new InvalidOperationException("Log vanished after insert.");
        return ToDto(saved);
    }

    public async Task<PagedResult<LogDto>> ListAsync(Guid automationId, int page, int pageSize, CancellationToken ct)
    {
        await EnsureAutomationAsync(automationId, ct);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var (items, total) = await logs.GetPageAsync(automationId, page, pageSize, ct);
        return new PagedResult<LogDto>(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    /// <summary>Requires exactly one valid value per live input metric. Errors are keyed by metric id for the form.</summary>
    private static Dictionary<Guid, decimal> Validate(ReportLogRequest request, List<Domain.Entities.MetricDefinition> inputs)
    {
        var errors = new Dictionary<string, string[]>();
        var byId = inputs.ToDictionary(d => d.Id);
        var values = new Dictionary<Guid, decimal>();

        if (request.Values is null || request.Values.Count == 0)
            errors["values"] = ["Provide a value for each input metric."];

        foreach (var v in request.Values ?? [])
        {
            if (!byId.TryGetValue(v.MetricId, out var def))
            {
                errors[v.MetricId.ToString()] = ["Not an input metric of this automation."];
                continue;
            }
            if (values.ContainsKey(def.Id))
            {
                errors[def.Id.ToString()] = ["Provided more than once."];
                continue;
            }

            var problem = CheckValue(def.ValueType, v.Value);
            if (problem is not null) errors[def.Id.ToString()] = [problem];
            else values[def.Id] = v.Value;
        }

        foreach (var def in inputs)
            if (!values.ContainsKey(def.Id) && !errors.ContainsKey(def.Id.ToString()))
                errors[def.Id.ToString()] = [$"A value for \"{def.Label}\" is required."];

        if (errors.Count > 0) throw new ValidationFailedException(errors);
        return values;
    }

    private static string? CheckValue(MetricValueType type, decimal value)
    {
        if (Math.Abs(value) >= MaxMagnitude) return "The value is too large.";
        if (Scale(value) > MaxScale) return $"At most {MaxScale} decimal places are allowed.";
        if (type == MetricValueType.Duration && value < 0) return "A duration cannot be negative.";
        return null;
    }

    // Dividing by 1.000... strips trailing zeros, so 5.0000000000000 counts as scale 0 rather than 13.
    private static int Scale(decimal value) => (decimal.GetBits(value / 1.0000000000000000000000000000m)[3] >> 16) & 0xFF;

    private async Task EnsureAutomationAsync(Guid automationId, CancellationToken ct)
    {
        if (!await metrics.AutomationExistsAsync(automationId, ct))
            throw new NotFoundException("Automation not found.");
    }

    internal static LogDto ToDto(LogWithReporter r) => new(
        r.Log.Id,
        r.Log.ReportedAt,
        r.Reporter,
        r.Log.Values
            .OrderBy(v => v.Role) // inputs first, then computed
            .Select(v => new LogValueDto(v.MetricDefinitionId, v.LabelSnapshot, v.ValueTypeSnapshot,
                v.CurrencyCodeSnapshot, v.Role, v.Value, v.FormulaSnapshot))
            .ToList());
}
