namespace Metrics.Application.Formulas;

public sealed class FormulaEngine : IFormulaEngine
{
    public const int MaxLength = 500;

    // Matches the NUMERIC(28,10) storage of log values, so a stored result equals what was computed.
    private const int ResultScale = 10;

    public FormulaAnalysis Analyze(string? formula, IReadOnlyDictionary<string, MetricType> inputTypes)
    {
        if (string.IsNullOrWhiteSpace(formula))
            return Invalid(new FormulaError(FormulaErrorCode.EmptyFormula, "The formula is empty.", 1));

        if (formula.Length > MaxLength)
            return Invalid(new FormulaError(FormulaErrorCode.TooLong, $"The formula is longer than {MaxLength} characters.", MaxLength + 1));

        Expr ast;
        try
        {
            ast = Parser.Parse(formula);
        }
        catch (FormulaSyntaxException ex)
        {
            return Invalid(ex.Error);
        }

        var checker = new TypeChecker(inputTypes);
        var type = checker.Check(ast);

        if (checker.Errors.Count == 0 && checker.References.Count == 0)
            checker.Errors.Add(new FormulaError(FormulaErrorCode.NoMetricReference,
                "A computed metric must reference at least one input metric.", 1));

        var errors = checker.Errors.OrderBy(e => e.Position).ToList();
        return errors.Count == 0
            ? new FormulaAnalysis(true, type, checker.References, errors)
            : new FormulaAnalysis(false, null, checker.References, errors);
    }

    public decimal? Evaluate(
        string formula, IReadOnlyDictionary<string, decimal> values, IReadOnlyDictionary<string, MetricType> inputTypes)
    {
        Expr ast;
        try
        {
            ast = Parser.Parse(formula);
        }
        catch (FormulaSyntaxException ex)
        {
            throw new InvalidOperationException($"Invalid formula: {ex.Error.Message}", ex);
        }

        var result = Evaluator.Evaluate(ast, values, inputTypes);
        return result is null ? null : Math.Round(result.Value, ResultScale);
    }

    private static FormulaAnalysis Invalid(FormulaError error) => new(false, null, [], [error]);
}
