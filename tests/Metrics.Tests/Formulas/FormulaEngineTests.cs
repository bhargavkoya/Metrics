using Metrics.Application.Formulas;
using Metrics.Domain;

namespace Metrics.Tests.Formulas;

public class FormulaAnalyzeTests
{
    private readonly FormulaEngine _engine = new();

    private static readonly Dictionary<string, MetricType> Inputs = new()
    {
        ["Manual Time"] = MetricType.Duration,
        ["Auto Time"] = MetricType.Duration,
        ["Records"] = MetricType.Number,
        ["Cost Before"] = MetricType.Currency("USD"),
        ["Cost After"] = MetricType.Currency("USD"),
        ["Cost EUR"] = MetricType.Currency("EUR"),
        ["Rate"] = MetricType.Percentage,
    };

    private FormulaAnalysis Analyze(string f) => _engine.Analyze(f, Inputs);

    [Theory]
    [InlineData("[Manual Time] - [Auto Time]", MetricValueType.Duration, null)]
    [InlineData("([Cost Before] - [Cost After]) / [Cost Before] * 100", MetricValueType.Number, null)]
    [InlineData("[Cost Before] * [Rate]", MetricValueType.Currency, "USD")]
    [InlineData("[Rate] * [Cost Before]", MetricValueType.Currency, "USD")]
    [InlineData("[Manual Time] * [Rate]", MetricValueType.Duration, null)]
    [InlineData("[Cost Before] * [Records]", MetricValueType.Currency, "USD")]
    [InlineData("[Records] * 2 + [Records]", MetricValueType.Number, null)]
    [InlineData("-[Manual Time]", MetricValueType.Duration, null)]
    [InlineData("[Manual Time] / [Auto Time]", MetricValueType.Number, null)]
    [InlineData("[Manual Time] / 2", MetricValueType.Duration, null)]
    [InlineData("[manual time] - [AUTO TIME]", MetricValueType.Duration, null)] // references ignore case
    [InlineData("[Records] × 2 ÷ 4", MetricValueType.Number, null)]
    public void ValidFormulas_InferResultType(string formula, MetricValueType kind, string? currency)
    {
        var a = Analyze(formula);

        Assert.True(a.IsValid, string.Join("; ", a.Errors.Select(e => e.Message)));
        Assert.Equal(kind, a.ResultType!.Value.Kind);
        Assert.Equal(currency, a.ResultType!.Value.CurrencyCode);
    }

    [Theory]
    [InlineData("[Records] / [Auto Time]", FormulaErrorCode.IncompatibleTypes, "Cannot divide Number by Duration")]
    [InlineData("[Cost Before] * [Cost After]", FormulaErrorCode.IncompatibleTypes, "Cannot multiply Currency (USD) by Currency (USD)")]
    [InlineData("[Cost Before] + [Auto Time]", FormulaErrorCode.IncompatibleTypes, "Cannot add Currency (USD) and Duration")]
    [InlineData("[Cost Before] - [Auto Time]", FormulaErrorCode.IncompatibleTypes, "Cannot subtract Duration from Currency (USD)")]
    [InlineData("[Records] + [Auto Time]", FormulaErrorCode.IncompatibleTypes, "Cannot add Number and Duration")]
    [InlineData("[Cost Before] + [Cost EUR]", FormulaErrorCode.IncompatibleCurrency, "currencies differ")]
    [InlineData("[Cost Before] / [Cost EUR]", FormulaErrorCode.IncompatibleCurrency, "currencies differ")]
    [InlineData("[Rate] * [Rate]", FormulaErrorCode.IncompatibleTypes, "Cannot multiply Percentage by Percentage")]
    [InlineData("[Records] / [Rate]", FormulaErrorCode.IncompatibleTypes, "Cannot divide Number by Percentage")]
    [InlineData("[Missing] + 1", FormulaErrorCode.UnknownMetric, "Unknown metric [Missing]")]
    [InlineData("[Records] + [Nope]", FormulaErrorCode.UnknownMetric, "Unknown metric [Nope]")]
    [InlineData("[Records] / 0", FormulaErrorCode.DivisionByZeroLiteral, "Division by zero")]
    [InlineData("[Records] / 0.00", FormulaErrorCode.DivisionByZeroLiteral, "Division by zero")]
    [InlineData("[Records] / (3 - 3)", FormulaErrorCode.DivisionByZeroLiteral, "Division by zero")]
    [InlineData("[Records] / (2 * 0 + 0)", FormulaErrorCode.DivisionByZeroLiteral, "Division by zero")]
    [InlineData("[Records] / -0", FormulaErrorCode.DivisionByZeroLiteral, "Division by zero")]
    [InlineData("1 + 2", FormulaErrorCode.NoMetricReference, "at least one input metric")]
    [InlineData("", FormulaErrorCode.EmptyFormula, "empty")]
    [InlineData("[Records] +", FormulaErrorCode.SyntaxError, "ends unexpectedly")]
    public void InvalidFormulas_ReportTheRightError(string formula, FormulaErrorCode code, string messagePart)
    {
        var a = Analyze(formula);

        Assert.False(a.IsValid);
        Assert.Null(a.ResultType);
        Assert.Contains(a.Errors, e => e.Code == code && e.Message.Contains(messagePart));
    }

    [Fact]
    public void NonZeroLiteralDivisor_IsFine_AndDataDependentDivisorIsNotFlagged()
    {
        Assert.True(Analyze("[Records] / 2").IsValid);
        Assert.True(Analyze("[Records] / (3 - 2)").IsValid);
        Assert.True(Analyze("[Records] / ([Records] - [Records])").IsValid); // zero only at runtime
    }

    [Fact]
    public void ErrorPositions_PointAtTheOffendingToken()
    {
        Assert.Equal(1, Analyze("[Missing] + 1").Errors.Single().Position);
        Assert.Equal(11, Analyze("[Records] / [Auto Time]").Errors.Single().Position);          // the '/' operator
        Assert.Equal(13, Analyze("[Records] / 0").Errors.Single().Position);                      // the zero literal
        Assert.Equal(13, Analyze("[Records] + [Nope]").Errors.Single().Position);                 // the unknown reference
    }

    [Fact]
    public void MultipleIndependentErrors_AreAllReported_InPositionOrder()
    {
        var a = Analyze("[Nope1] + [Nope2] / 0");

        Assert.Equal(3, a.Errors.Count);
        Assert.Equal(a.Errors.OrderBy(e => e.Position).ToList(), a.Errors.ToList());
    }

    [Fact]
    public void ErrorInOneOperand_DoesNotCascadeIntoTypeErrors()
    {
        var a = Analyze("[Nope] * [Cost Before]");

        Assert.Single(a.Errors);
        Assert.Equal(FormulaErrorCode.UnknownMetric, a.Errors[0].Code);
    }

    [Fact]
    public void References_AreDistinct_AndUseCanonicalLabels()
    {
        var a = Analyze("[records] * 2 + [RECORDS] + [Auto Time] / [Auto Time]");

        Assert.Equal(["Records", "Auto Time"], a.References);
    }

    [Fact]
    public void TooLong_IsRejected()
    {
        var a = Analyze("[Records]" + string.Concat(Enumerable.Repeat(" + 1", 200)));

        Assert.Contains(a.Errors, e => e.Code == FormulaErrorCode.TooLong);
    }

    [Fact]
    public void Formula_ReferencingNothingAvailable_ReportsUnknownNotNoReference()
    {
        var a = _engine.Analyze("[Anything] + 1", new Dictionary<string, MetricType>());

        Assert.Equal(FormulaErrorCode.UnknownMetric, a.Errors.Single().Code);
    }
}

/// <summary>
/// Exhaustive operator x type-pair matrix, checked against an independent restatement of the rules
/// (not the production TypeRules), so a regression in either one shows up.
/// </summary>
public class FormulaTypeMatrixTests
{
    private static readonly (string Name, MetricType Type)[] Types =
    [
        ("N", MetricType.Number),
        ("P", MetricType.Percentage),
        ("C", MetricType.Currency("USD")),
        ("D", MetricType.Duration),
    ];

    // Expected result kind for (op, left, right), or null when the combination must be rejected.
    private static string? Expected(char op, string l, string r) => op switch
    {
        '+' or '-' => l == r ? l : null,
        '*' => (l, r) switch
        {
            ("N", "N") => "N",
            ("N", _) => r,
            (_, "N") => l,
            ("C", "P") or ("D", "P") => l,
            ("P", "C") or ("P", "D") => r,
            _ => null,
        },
        '/' => (l, r) switch
        {
            ("N", "N") => "N",
            ("C", "C") or ("D", "D") or ("P", "P") => "N",
            ("C", "N") or ("D", "N") or ("P", "N") => l,
            _ => null,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    public static IEnumerable<object[]> AllCombinations() =>
        from op in new[] { '+', '-', '*', '/' }
        from l in Types
        from r in Types
        select new object[] { op, l.Name, r.Name };

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void EveryCombination_MatchesTheRulesTable(char op, string l, string r)
    {
        var inputs = new Dictionary<string, MetricType>
        {
            ["L"] = Types.Single(t => t.Name == l).Type,
            ["R"] = Types.Single(t => t.Name == r).Type,
        };

        var a = new FormulaEngine().Analyze($"[L] {op} [R]", inputs);

        var expected = Expected(op, l, r);
        if (expected is null)
        {
            Assert.False(a.IsValid, $"[{l}] {op} [{r}] should be rejected");
            Assert.Contains(a.Errors, e => e.Code == FormulaErrorCode.IncompatibleTypes);
        }
        else
        {
            Assert.True(a.IsValid, $"[{l}] {op} [{r}] should be accepted: {string.Join("; ", a.Errors.Select(e => e.Message))}");
            Assert.Equal(Types.Single(t => t.Name == expected).Type.Kind, a.ResultType!.Value.Kind);
        }
    }

    [Theory]
    [InlineData('+')]
    [InlineData('-')]
    [InlineData('/')]
    public void SameKindCurrencies_WithDifferentCodes_AreRejected(char op)
    {
        var inputs = new Dictionary<string, MetricType> { ["L"] = MetricType.Currency("USD"), ["R"] = MetricType.Currency("EUR") };

        var a = new FormulaEngine().Analyze($"[L] {op} [R]", inputs);

        Assert.Equal(FormulaErrorCode.IncompatibleCurrency, a.Errors.Single().Code);
    }

    [Fact]
    public void CurrencyCode_SurvivesScalingByNumberAndPercentage()
    {
        var inputs = new Dictionary<string, MetricType> { ["C"] = MetricType.Currency("GBP"), ["P"] = MetricType.Percentage, ["N"] = MetricType.Number };

        Assert.Equal("GBP", new FormulaEngine().Analyze("[C] * [N]", inputs).ResultType!.Value.CurrencyCode);
        Assert.Equal("GBP", new FormulaEngine().Analyze("[P] * [C]", inputs).ResultType!.Value.CurrencyCode);
        Assert.Equal("GBP", new FormulaEngine().Analyze("[C] / [N]", inputs).ResultType!.Value.CurrencyCode);
        Assert.Null(new FormulaEngine().Analyze("[C] / [C]", inputs).ResultType!.Value.CurrencyCode); // ratio is a plain Number
    }
}

public class FormulaEvaluateTests
{
    private readonly FormulaEngine _engine = new();

    private static readonly Dictionary<string, MetricType> Types = new()
    {
        ["A"] = MetricType.Number,
        ["B"] = MetricType.Number,
        ["Manual"] = MetricType.Duration,
        ["Auto"] = MetricType.Duration,
        ["Before"] = MetricType.Currency("USD"),
        ["After"] = MetricType.Currency("USD"),
        ["Rate"] = MetricType.Percentage,
    };

    private decimal? Eval(string formula, params (string Label, decimal Value)[] values) =>
        _engine.Evaluate(formula, values.ToDictionary(v => v.Label, v => v.Value), Types);

    [Fact]
    public void Arithmetic_FollowsPrecedence_AndParentheses()
    {
        Assert.Equal(13m, Eval("[A] + 3 * 4", ("A", 1)));
        Assert.Equal(16m, Eval("([A] + 3) * 4", ("A", 1)));
        Assert.Equal(5m, Eval("[A] - 3 - 2", ("A", 10)));      // left associative: (10-3)-2
        Assert.Equal(10m, Eval("[A] / 2 / 5", ("A", 100)));    // (100/2)/5
        Assert.Equal(7m, Eval("[A] * 2 - [B]", ("A", 5), ("B", 3)));
    }

    [Fact]
    public void UnaryMinus_Works()
    {
        Assert.Equal(3m, Eval("-[A] + 5", ("A", 2)));
        Assert.Equal(2m, Eval("--[A]", ("A", 2)));
        Assert.Equal(-6m, Eval("-[A] * 3", ("A", 2)));
    }

    [Fact]
    public void Durations_SubtractInTheirStoredUnit()
    {
        Assert.Equal(3000m, Eval("[Manual] - [Auto]", ("Manual", 3600), ("Auto", 600)));
        Assert.Equal(6m, Eval("[Manual] / [Auto]", ("Manual", 3600), ("Auto", 600))); // ratio -> Number
    }

    [Fact]
    public void Percentage_TimesCurrency_ScalesByOneHundred_InEitherOrder()
    {
        Assert.Equal(50m, Eval("[Before] * [Rate]", ("Before", 200), ("Rate", 25)));
        Assert.Equal(50m, Eval("[Rate] * [Before]", ("Before", 200), ("Rate", 25)));
    }

    [Fact]
    public void Percentage_TimesDuration_Scales_ButNumberTimesPercentage_DoesNot()
    {
        Assert.Equal(500m, Eval("[Manual] * [Rate]", ("Manual", 1000), ("Rate", 50)));
        Assert.Equal(20m, Eval("[A] * [Rate]", ("A", 2), ("Rate", 10))); // N x P -> P, stays in percent points
    }

    [Fact]
    public void SavingsRatio_FromCurrencies()
    {
        // (100 - 60) / 100 * 100 = 40
        Assert.Equal(40m, Eval("([Before] - [After]) / [Before] * 100", ("Before", 100), ("After", 60)));
    }

    [Fact]
    public void DivisionByZeroFromData_ReturnsNull_NotAnException()
    {
        Assert.Null(Eval("[A] / [B]", ("A", 5), ("B", 0)));
        Assert.Null(Eval("[A] / ([B] - [B])", ("A", 5), ("B", 3)));
        Assert.Null(Eval("1 + [A] / [B]", ("A", 5), ("B", 0))); // null propagates through the whole expression
    }

    [Fact]
    public void Overflow_ReturnsNull()
    {
        Assert.Null(Eval("[A] * [A]", ("A", decimal.MaxValue)));
    }

    [Fact]
    public void Result_IsRoundedToStorageScale()
    {
        Assert.Equal(0.3333333333m, Eval("[A] / 3", ("A", 1)));
        Assert.Equal(0.6666666667m, Eval("[A] / 3 * 2", ("A", 1)));
    }

    [Fact]
    public void References_IgnoreCase_AndAliasesWork()
    {
        Assert.Equal(6m, Eval("[a] × [B] ÷ 2", ("A", 3), ("B", 4)));
    }

    [Fact]
    public void MissingValue_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Eval("[A] + [B]", ("A", 1)));
    }

    [Fact]
    public void UnparseableFormula_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Eval("[A] +", ("A", 1)));
    }

    [Fact]
    public void Evaluating_DoesNotMutateInputs_AndIsRepeatable()
    {
        var values = new Dictionary<string, decimal> { ["A"] = 4 };

        var first = _engine.Evaluate("[A] * 2", values, Types);
        var second = _engine.Evaluate("[A] * 2", values, Types);

        Assert.Equal(8m, first);
        Assert.Equal(first, second);
        Assert.Single(values);
    }
}
