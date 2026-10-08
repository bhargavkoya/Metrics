using Metrics.Application.Common;
using Metrics.Application.Formulas;
using Metrics.Domain;
using Metrics.Domain.Entities;

namespace Metrics.Application.Metrics;

public class MetricService(IMetricRepository repo, IFormulaEngine engine, TimeProvider clock) : IMetricService
{
    public const int MaxLabelLength = 100;
    public const string DefaultCurrency = "USD";

    public async Task<IReadOnlyList<MetricDefinitionDto>> ListAsync(Guid automationId, CancellationToken ct)
    {
        await EnsureAutomationAsync(automationId, ct);
        return (await repo.ListLiveAsync(automationId, ct)).Select(ToDto).ToList();
    }

    public async Task<MetricDefinitionDto> CreateAsync(
        Guid automationId, Guid userId, CreateMetricRequest request, CancellationToken ct)
    {
        await EnsureAutomationAsync(automationId, ct);

        var label = (request.Label ?? "").Trim();
        var errors = new Dictionary<string, string[]>();
        ValidateLabel(label, errors);

        var live = await repo.ListLiveAsync(automationId, ct);
        if (errors.Count == 0 && live.Any(m => string.Equals(m.Label, label, StringComparison.OrdinalIgnoreCase)))
            throw new ConflictException($"A metric labelled \"{label}\" already exists on this automation.");

        var def = new MetricDefinition
        {
            Id = Guid.NewGuid(),
            AutomationId = automationId,
            Label = label,
            Kind = request.Kind,
            CreatedBy = userId,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };

        switch (request.Kind)
        {
            case MetricKind.Input:
                BuildInput(def, request, errors);
                break;
            case MetricKind.Computed:
                BuildComputed(def, request, InputTypes(live), errors);
                break;
            default:
                errors["kind"] = ["Kind must be Input or Computed."];
                break;
        }

        if (errors.Count > 0) throw new ValidationFailedException(errors);

        await repo.AddAsync(def, ct);
        return ToDto(def);
    }

    public async Task<ValidateFormulaResponse> ValidateFormulaAsync(
        Guid automationId, ValidateFormulaRequest request, CancellationToken ct)
    {
        await EnsureAutomationAsync(automationId, ct);
        var live = await repo.ListLiveAsync(automationId, ct);

        var analysis = engine.Analyze(request.Formula, InputTypes(live));
        return new ValidateFormulaResponse(
            analysis.IsValid,
            analysis.ResultType?.Kind,
            analysis.ResultType?.CurrencyCode,
            analysis.References,
            analysis.Errors.Select(e => new FormulaErrorDto(e.Code.ToString(), e.Message, e.Position)).ToList());
    }

    public async Task DeleteAsync(Guid automationId, Guid metricId, CancellationToken ct)
    {
        await EnsureAutomationAsync(automationId, ct);
        var def = await repo.FindLiveAsync(automationId, metricId, ct)
                  ?? throw new NotFoundException("Metric not found.");

        if (def.Kind == MetricKind.Input)
        {
            var live = await repo.ListLiveAsync(automationId, ct);
            var inputs = InputTypes(live);
            var dependents = live
                .Where(m => m.Kind == MetricKind.Computed && m.FormulaText is not null)
                .Where(m => engine.Analyze(m.FormulaText, inputs).References
                    .Contains(def.Label, StringComparer.OrdinalIgnoreCase))
                .Select(m => $"\"{m.Label}\"")
                .ToList();

            if (dependents.Count > 0)
                throw new ConflictException(
                    $"\"{def.Label}\" is used by the computed metric(s) {string.Join(", ", dependents)}. Delete those first.");
        }

        // Soft delete: removes the metric going forward; logs already recorded keep their snapshotted values.
        await repo.SoftDeleteAsync(def, clock.GetUtcNow().UtcDateTime, ct);
    }

    private static void ValidateLabel(string label, Dictionary<string, string[]> errors)
    {
        if (label.Length == 0 || label.Length > MaxLabelLength)
            errors["label"] = [$"Label is required and must be {MaxLabelLength} characters or fewer."];
        else if (label.Contains('[') || label.Contains(']'))
            errors["label"] = ["Label cannot contain '[' or ']' (they delimit metric references in formulas)."];
        else if (label.Any(char.IsControl))
            errors["label"] = ["Label cannot contain control characters."];
    }

    private static void BuildInput(MetricDefinition def, CreateMetricRequest r, Dictionary<string, string[]> errors)
    {
        if (!string.IsNullOrWhiteSpace(r.Formula))
            errors["formula"] = ["Input metrics cannot have a formula."];

        if (r.ValueType is not { } type || !Enum.IsDefined(type))
        {
            errors["valueType"] = ["Value type must be Number, Percentage, Currency or Duration."];
            return;
        }
        def.ValueType = type;

        if (type != MetricValueType.Currency)
        {
            if (!string.IsNullOrWhiteSpace(r.CurrencyCode)) errors["currencyCode"] = ["Currency code applies to Currency metrics only."];
            return;
        }

        var code = string.IsNullOrWhiteSpace(r.CurrencyCode) ? DefaultCurrency : r.CurrencyCode.Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
            errors["currencyCode"] = ["Currency code must be a 3-letter code such as USD."];
        else
            def.CurrencyCode = code;
    }

    private void BuildComputed(
        MetricDefinition def, CreateMetricRequest r, Dictionary<string, MetricType> inputs, Dictionary<string, string[]> errors)
    {
        var formula = r.Formula?.Trim();
        var analysis = engine.Analyze(formula, inputs);
        if (!analysis.IsValid)
        {
            errors["formula"] = analysis.Errors.Select(e => $"{e.Message} (position {e.Position})").ToArray();
            return;
        }

        var result = analysis.ResultType!.Value;
        def.FormulaText = formula;
        def.ValueType = result.Kind;
        def.CurrencyCode = result.CurrencyCode;
    }

    private static Dictionary<string, MetricType> InputTypes(IEnumerable<MetricDefinition> live) =>
        live.Where(m => m.Kind == MetricKind.Input)
            .ToDictionary(m => m.Label, m => new MetricType(m.ValueType, m.CurrencyCode), StringComparer.OrdinalIgnoreCase);

    private async Task EnsureAutomationAsync(Guid automationId, CancellationToken ct)
    {
        if (!await repo.AutomationExistsAsync(automationId, ct))
            throw new NotFoundException("Automation not found.");
    }

    private static MetricDefinitionDto ToDto(MetricDefinition m) =>
        new(m.Id, m.Label, m.Kind, m.ValueType, m.CurrencyCode, m.FormulaText, m.CreatedAt);
}
