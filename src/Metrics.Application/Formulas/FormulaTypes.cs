using Metrics.Domain;

namespace Metrics.Application.Formulas;

/// <summary>A metric's type. Currency carries its ISO code so USD and EUR amounts can't be mixed.</summary>
public readonly record struct MetricType(MetricValueType Kind, string? CurrencyCode = null)
{
    public static MetricType Number => new(MetricValueType.Number);
    public static MetricType Percentage => new(MetricValueType.Percentage);
    public static MetricType Duration => new(MetricValueType.Duration);
    public static MetricType Currency(string code) => new(MetricValueType.Currency, code);

    public override string ToString() =>
        Kind == MetricValueType.Currency && CurrencyCode is not null ? $"Currency ({CurrencyCode})" : Kind.ToString();
}

public enum FormulaErrorCode
{
    EmptyFormula,
    TooLong,
    TooDeep,
    SyntaxError,
    UnknownMetric,
    IncompatibleTypes,
    IncompatibleCurrency,
    DivisionByZeroLiteral,
    NoMetricReference
}

/// <param name="Position">1-based character position in the formula text.</param>
public record FormulaError(FormulaErrorCode Code, string Message, int Position);

public record FormulaAnalysis(
    bool IsValid,
    MetricType? ResultType,
    IReadOnlyList<string> References,
    IReadOnlyList<FormulaError> Errors);

public interface IFormulaEngine
{
    /// <summary>Parses and type-checks a formula against the available input metrics. Never throws for bad formulas.</summary>
    FormulaAnalysis Analyze(string? formula, IReadOnlyDictionary<string, MetricType> inputTypes);

    /// <summary>
    /// Evaluates a formula that already passed <see cref="Analyze"/>. Returns null when the data makes the result undefined
    /// (division by zero, overflow). Throws <see cref="InvalidOperationException"/> if the formula or inputs are invalid.
    /// </summary>
    decimal? Evaluate(string formula, IReadOnlyDictionary<string, decimal> values, IReadOnlyDictionary<string, MetricType> inputTypes);
}
