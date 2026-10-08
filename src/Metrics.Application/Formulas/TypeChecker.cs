namespace Metrics.Application.Formulas;

/// <summary>Walks the AST once, resolving references and applying <see cref="TypeRules"/>. Collects all type errors.</summary>
internal sealed class TypeChecker
{
    private readonly Dictionary<string, (string Canonical, MetricType Type)> _inputs;
    private readonly HashSet<string> _references = new(StringComparer.OrdinalIgnoreCase);

    public TypeChecker(IReadOnlyDictionary<string, MetricType> inputs)
    {
        // Labels are unique case-insensitively, so lookups ignore case; keep the canonical casing for reporting.
        _inputs = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (label, type) in inputs) _inputs[label] = (label, type);
    }

    public List<FormulaError> Errors { get; } = [];

    /// <summary>Canonical labels of the input metrics the formula references, in first-use order.</summary>
    public List<string> References { get; } = [];

    /// <summary>Returns the node's type, or null when the subtree had an error (so errors don't cascade).</summary>
    public MetricType? Check(Expr e)
    {
        switch (e)
        {
            case NumberLit:
                return MetricType.Number;

            case MetricRef r:
                if (!_inputs.TryGetValue(r.Label, out var found))
                {
                    Errors.Add(new FormulaError(FormulaErrorCode.UnknownMetric,
                        $"Unknown metric [{r.Label}]. Formulas can only reference input metrics defined on this automation.", r.Position));
                    return null;
                }
                if (_references.Add(found.Canonical)) References.Add(found.Canonical);
                return found.Type;

            case Negate n:
                return Check(n.Operand);

            case Binary b:
                var left = Check(b.Left);
                var right = Check(b.Right);

                if (b.Op == '/' && ConstantFold(b.Right) == 0m)
                    Errors.Add(new FormulaError(FormulaErrorCode.DivisionByZeroLiteral, "Division by zero.", b.Right.Position));

                if (left is null || right is null) return null;

                var result = TypeRules.Combine(b.Op, left.Value, right.Value, out var code, out var message);
                if (result is null) Errors.Add(new FormulaError(code, message, b.Position));
                return result;

            default:
                throw new InvalidOperationException($"Unknown node {e.GetType().Name}.");
        }
    }

    /// <summary>Evaluates literal-only subtrees (so "/(2-2)" is caught); null when not constant or undefined.</summary>
    internal static decimal? ConstantFold(Expr e)
    {
        try
        {
            return e switch
            {
                NumberLit n => n.Value,
                Negate neg => -ConstantFold(neg.Operand),
                Binary b => FoldBinary(b),
                _ => null
            };
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static decimal? FoldBinary(Binary b)
    {
        var l = ConstantFold(b.Left);
        var r = ConstantFold(b.Right);
        if (l is null || r is null) return null;
        return b.Op switch
        {
            '+' => l + r,
            '-' => l - r,
            '*' => l * r,
            '/' => r == 0m ? null : l / r,
            _ => null
        };
    }
}
