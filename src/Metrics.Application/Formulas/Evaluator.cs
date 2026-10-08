namespace Metrics.Application.Formulas;

/// <summary>
/// Typed evaluator: carries each value's type so percentages scale correctly (200 USD * 25% = 50 USD).
/// Percentages are stored as percent points (25 means 25%). Returns null when the data makes the result undefined.
/// </summary>
internal static class Evaluator
{
    private readonly record struct Val(decimal Value, MetricType Type);

    public static decimal? Evaluate(
        Expr expr,
        IReadOnlyDictionary<string, decimal> values,
        IReadOnlyDictionary<string, MetricType> types)
    {
        var v = new Dictionary<string, decimal>(values, StringComparer.OrdinalIgnoreCase);
        var t = new Dictionary<string, MetricType>(types, StringComparer.OrdinalIgnoreCase);
        try
        {
            return Eval(expr, v, t)?.Value;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static Val? Eval(Expr e, Dictionary<string, decimal> values, Dictionary<string, MetricType> types)
    {
        switch (e)
        {
            case NumberLit n:
                return new Val(n.Value, MetricType.Number);

            case MetricRef r:
                if (!types.TryGetValue(r.Label, out var type))
                    throw new InvalidOperationException($"Unknown metric [{r.Label}].");
                if (!values.TryGetValue(r.Label, out var value))
                    throw new InvalidOperationException($"No value supplied for [{r.Label}].");
                return new Val(value, type);

            case Negate neg:
                return Eval(neg.Operand, values, types) is { } o ? o with { Value = -o.Value } : null;

            case Binary b:
                if (Eval(b.Left, values, types) is not { } l || Eval(b.Right, values, types) is not { } rr) return null;

                var resultType = TypeRules.Combine(b.Op, l.Type, rr.Type, out _, out var message)
                                 ?? throw new InvalidOperationException(message);

                decimal result;
                switch (b.Op)
                {
                    case '+': result = l.Value + rr.Value; break;
                    case '-': result = l.Value - rr.Value; break;
                    case '*':
                        result = l.Value * rr.Value;
                        if (TypeRules.NeedsPercentScaling(l.Type, rr.Type)) result /= 100m;
                        break;
                    default:
                        if (rr.Value == 0m) return null;
                        result = l.Value / rr.Value;
                        break;
                }
                return new Val(result, resultType);

            default:
                throw new InvalidOperationException($"Unknown node {e.GetType().Name}.");
        }
    }
}
