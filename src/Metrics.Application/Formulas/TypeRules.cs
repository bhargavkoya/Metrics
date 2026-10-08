using Metrics.Domain;
using static Metrics.Domain.MetricValueType;

namespace Metrics.Application.Formulas;

/// <summary>
/// The single source of truth for which operand types combine under each operator, and what they produce.
/// Used by the type checker (save time) and the evaluator (percentage scaling).
/// <code>
/// + -   same type only
/// *     N*N, N*C, N*D, N*P (either order), C*P, D*P (either order)
/// /     N/N, C/C, D/D, P/P (-> Number), C/N, D/N, P/N (keep type)
/// </code>
/// </summary>
internal static class TypeRules
{
    public static MetricType? Combine(char op, MetricType l, MetricType r, out FormulaErrorCode code, out string message)
    {
        code = FormulaErrorCode.IncompatibleTypes;
        message = "";

        switch (op)
        {
            case '+' or '-':
                if (l.Kind != r.Kind)
                {
                    message = op == '+' ? $"Cannot add {l} and {r}." : $"Cannot subtract {r} from {l}.";
                    return null;
                }
                if (!SameCurrency(l, r, out message)) { code = FormulaErrorCode.IncompatibleCurrency; return null; }
                return l;

            case '*':
                var product = Multiply(l, r);
                if (product is null) message = $"Cannot multiply {l} by {r}.";
                return product;

            case '/':
                if (l.Kind == r.Kind && l.Kind != Number && !SameCurrency(l, r, out message))
                {
                    code = FormulaErrorCode.IncompatibleCurrency;
                    return null;
                }
                var quotient = Divide(l, r);
                if (quotient is null) message = $"Cannot divide {l} by {r}.";
                return quotient;

            default:
                throw new ArgumentOutOfRangeException(nameof(op), op, "Unsupported operator.");
        }
    }

    /// <summary>True when a percentage multiplies a currency or duration, which needs the 1/100 scaling.</summary>
    public static bool NeedsPercentScaling(MetricType l, MetricType r) =>
        (l.Kind == Percentage && r.Kind is Currency or Duration) ||
        (r.Kind == Percentage && l.Kind is Currency or Duration);

    private static MetricType? Multiply(MetricType l, MetricType r) => (l.Kind, r.Kind) switch
    {
        (Number, Number) => MetricType.Number,
        (Number, _) => r,
        (_, Number) => l,
        (Currency or Duration, Percentage) => l,
        (Percentage, Currency or Duration) => r,
        _ => null
    };

    private static MetricType? Divide(MetricType l, MetricType r) => (l.Kind, r.Kind) switch
    {
        (Number, Number) => MetricType.Number,
        (Currency, Currency) or (Duration, Duration) or (Percentage, Percentage) => MetricType.Number,
        (Currency or Duration or Percentage, Number) => l,
        _ => null
    };

    private static bool SameCurrency(MetricType l, MetricType r, out string message)
    {
        message = "";
        if (l.Kind != Currency || l.CurrencyCode == r.CurrencyCode) return true;
        message = $"Cannot combine {l} with {r}: currencies differ.";
        return false;
    }
}
