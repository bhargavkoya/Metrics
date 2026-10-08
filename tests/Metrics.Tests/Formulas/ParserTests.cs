using Metrics.Application.Formulas;

namespace Metrics.Tests.Formulas;

public class TokenizerTests
{
    private static TokenKind[] Kinds(string s) => Tokenizer.Tokenize(s).Select(t => t.Kind).ToArray();

    [Fact]
    public void Tokenizes_AllTokenKinds()
    {
        Assert.Equal(
            [TokenKind.LParen, TokenKind.Ref, TokenKind.Minus, TokenKind.Number, TokenKind.RParen, TokenKind.Star,
             TokenKind.Number, TokenKind.Slash, TokenKind.Ref, TokenKind.Plus, TokenKind.Number, TokenKind.End],
            Kinds("([A] - 1.5) * 2 / [B] + .5"));
    }

    [Fact]
    public void MultiplicationAndDivisionAliases_AreAccepted()
    {
        Assert.Equal([TokenKind.Ref, TokenKind.Star, TokenKind.Ref, TokenKind.Slash, TokenKind.Ref, TokenKind.Minus, TokenKind.Number, TokenKind.End],
            Kinds("[A] × [B] ÷ [C] − 1"));
    }

    [Fact]
    public void Reference_KeepsInnerSpaces_TrimsEdges()
    {
        var t = Tokenizer.Tokenize("[  Time  Taken to Run ]")[0];

        Assert.Equal(TokenKind.Ref, t.Kind);
        Assert.Equal("Time  Taken to Run", t.Text);
    }

    [Fact]
    public void Numbers_ParseWithInvariantCulture()
    {
        Assert.Equal(1234.5m, Tokenizer.Tokenize("1234.5")[0].Number);
        Assert.Equal(0.5m, Tokenizer.Tokenize(".5")[0].Number);
    }

    [Theory]
    [InlineData("[A] $ 2", 5, "Unexpected character '$'")]
    [InlineData("[A", 1, "Missing closing ']'")]
    [InlineData("[]", 1, "Empty metric reference")]
    [InlineData("[ ]", 1, "Empty metric reference")]
    [InlineData("[A[B]]", 1, "nested")]
    [InlineData("1.2.3", 1, "more than one decimal point")]
    [InlineData("5. + 1", 1, "Invalid number")]
    [InlineData("1,5", 2, "Unexpected character ','")]
    [InlineData("Records + 1", 1, "Unexpected character 'R'")]
    [InlineData("]", 1, "Unexpected character ']'")]
    public void LexicalErrors_ReportPositionAndMessage(string input, int position, string messagePart)
    {
        var ex = Assert.Throws<FormulaSyntaxException>(() => Tokenizer.Tokenize(input));

        Assert.Equal(position, ex.Error.Position);
        Assert.Contains(messagePart, ex.Error.Message);
    }

    [Fact]
    public void NumberTooLargeForDecimal_IsSyntaxError()
    {
        Assert.Throws<FormulaSyntaxException>(() => Tokenizer.Tokenize(new string('9', 40)));
    }
}

public class ParserTests
{
    [Fact]
    public void MultiplicationBindsTighterThanAddition()
    {
        var e = Assert.IsType<Binary>(Parser.Parse("[A] + [B] * [C]"));

        Assert.Equal('+', e.Op);
        Assert.IsType<MetricRef>(e.Left);
        Assert.Equal('*', Assert.IsType<Binary>(e.Right).Op);
    }

    [Fact]
    public void Parentheses_OverridePrecedence()
    {
        var e = Assert.IsType<Binary>(Parser.Parse("([A] + [B]) * [C]"));

        Assert.Equal('*', e.Op);
        Assert.Equal('+', Assert.IsType<Binary>(e.Left).Op);
    }

    [Fact]
    public void SubtractionAndDivision_AreLeftAssociative()
    {
        var sub = Assert.IsType<Binary>(Parser.Parse("[A] - [B] - [C]"));
        Assert.Equal("A", Assert.IsType<MetricRef>(Assert.IsType<Binary>(sub.Left).Left).Label); // (A-B)-C
        Assert.Equal("C", Assert.IsType<MetricRef>(sub.Right).Label);

        var div = Assert.IsType<Binary>(Parser.Parse("[A] / [B] / [C]"));
        Assert.IsType<Binary>(div.Left);
        Assert.IsType<MetricRef>(div.Right);
    }

    [Fact]
    public void UnaryMinus_AppliesToOperand_AndCanStack()
    {
        var e = Assert.IsType<Negate>(Parser.Parse("--[A]"));
        Assert.IsType<Negate>(e.Operand);

        var mixed = Assert.IsType<Binary>(Parser.Parse("-[A] * [B]"));
        Assert.IsType<Negate>(mixed.Left); // (-A) * B
    }

    [Fact]
    public void BinaryNode_PositionIsTheOperator()
    {
        var e = Assert.IsType<Binary>(Parser.Parse("[A]  +  [B]"));

        Assert.Equal(6, e.Position);
    }

    [Theory]
    [InlineData("", FormulaErrorCode.EmptyFormula, 1)]
    [InlineData("   ", FormulaErrorCode.EmptyFormula, 1)]
    [InlineData("[A] +", FormulaErrorCode.SyntaxError, 6)]
    [InlineData("+ [A]", FormulaErrorCode.SyntaxError, 1)]
    [InlineData("[A] * * [B]", FormulaErrorCode.SyntaxError, 7)]
    [InlineData("([A] + [B]", FormulaErrorCode.SyntaxError, 11)]
    [InlineData("[A] + [B])", FormulaErrorCode.SyntaxError, 10)]
    [InlineData("()", FormulaErrorCode.SyntaxError, 2)]
    [InlineData("[A] [B]", FormulaErrorCode.SyntaxError, 5)]
    [InlineData("2 3", FormulaErrorCode.SyntaxError, 3)]
    public void SyntaxErrors_ReportCodeAndPosition(string input, FormulaErrorCode code, int position)
    {
        var ex = Assert.Throws<FormulaSyntaxException>(() => Parser.Parse(input));

        Assert.Equal(code, ex.Error.Code);
        Assert.Equal(position, ex.Error.Position);
    }

    [Fact]
    public void NestingAtTheLimit_Parses_OneBeyond_IsTooDeep()
    {
        var ok = new string('(', Parser.MaxDepth) + "[A]" + new string(')', Parser.MaxDepth);
        Parser.Parse(ok);

        var tooDeep = new string('(', Parser.MaxDepth + 1) + "[A]" + new string(')', Parser.MaxDepth + 1);
        var ex = Assert.Throws<FormulaSyntaxException>(() => Parser.Parse(tooDeep));
        Assert.Equal(FormulaErrorCode.TooDeep, ex.Error.Code);
    }

    [Fact]
    public void LongUnaryChain_IsTooDeep_NotAStackOverflow()
    {
        var ex = Assert.Throws<FormulaSyntaxException>(() => Parser.Parse(new string('-', 400) + "[A]"));

        Assert.Equal(FormulaErrorCode.TooDeep, ex.Error.Code);
    }
}
