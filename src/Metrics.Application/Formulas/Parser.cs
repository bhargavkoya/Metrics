namespace Metrics.Application.Formulas;

internal abstract record Expr(int Position);
internal sealed record NumberLit(int Position, decimal Value) : Expr(Position);
internal sealed record MetricRef(int Position, string Label) : Expr(Position);
internal sealed record Negate(int Position, Expr Operand) : Expr(Position);

/// <summary>Position is that of the operator token, so type errors point at the operator.</summary>
internal sealed record Binary(int Position, char Op, Expr Left, Expr Right) : Expr(Position);

/// <summary>
/// Recursive-descent parser.
/// <code>
/// expr    := term (('+'|'-') term)*
/// term    := unary (('*'|'/') unary)*
/// unary   := '-' unary | primary
/// primary := NUMBER | '[' label ']' | '(' expr ')'
/// </code>
/// </summary>
internal sealed class Parser
{
    public const int MaxDepth = 50;

    private readonly List<Token> _tokens;
    private int _index;
    private int _depth;

    private Parser(List<Token> tokens) => _tokens = tokens;

    public static Expr Parse(string text)
    {
        var tokens = Tokenizer.Tokenize(text);
        if (tokens[0].Kind == TokenKind.End)
            throw new FormulaSyntaxException(new FormulaError(FormulaErrorCode.EmptyFormula, "The formula is empty.", 1));

        var parser = new Parser(tokens);
        var expr = parser.ParseExpr();
        if (parser.Current.Kind != TokenKind.End)
            throw Syntax(parser.Current, $"Unexpected '{Describe(parser.Current)}'; expected an operator or the end of the formula.");
        return expr;
    }

    private Token Current => _tokens[_index];

    private Expr ParseExpr()
    {
        var left = ParseTerm();
        while (Current.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            var op = Current;
            _index++;
            left = new Binary(op.Position, op.Text[0], left, ParseTerm());
        }
        return left;
    }

    private Expr ParseTerm()
    {
        var left = ParseUnary();
        while (Current.Kind is TokenKind.Star or TokenKind.Slash)
        {
            var op = Current;
            _index++;
            left = new Binary(op.Position, op.Text[0], left, ParseUnary());
        }
        return left;
    }

    private Expr ParseUnary()
    {
        if (Current.Kind != TokenKind.Minus) return ParsePrimary();

        var minus = Current;
        Enter(minus);
        _index++;
        var operand = ParseUnary();
        _depth--;
        return new Negate(minus.Position, operand);
    }

    private Expr ParsePrimary()
    {
        var t = Current;
        switch (t.Kind)
        {
            case TokenKind.Number:
                _index++;
                return new NumberLit(t.Position, t.Number);
            case TokenKind.Ref:
                _index++;
                return new MetricRef(t.Position, t.Text);
            case TokenKind.LParen:
                Enter(t);
                _index++;
                var inner = ParseExpr();
                if (Current.Kind != TokenKind.RParen) throw Syntax(Current, "Missing closing ')'.");
                _index++;
                _depth--;
                return inner;
            default:
                throw Syntax(t, t.Kind == TokenKind.End
                    ? "The formula ends unexpectedly; expected a number, a [metric] or '('."
                    : $"Unexpected '{Describe(t)}'; expected a number, a [metric] or '('.");
        }
    }

    private void Enter(Token at)
    {
        if (++_depth > MaxDepth)
            throw new FormulaSyntaxException(new FormulaError(FormulaErrorCode.TooDeep,
                $"The formula is nested more than {MaxDepth} levels deep.", at.Position));
    }

    private static string Describe(Token t) => t.Kind == TokenKind.Ref ? $"[{t.Text}]" : t.Text;

    private static FormulaSyntaxException Syntax(Token at, string message) =>
        new(new FormulaError(FormulaErrorCode.SyntaxError, message, at.Position));
}
