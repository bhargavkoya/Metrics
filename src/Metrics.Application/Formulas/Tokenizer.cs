using System.Globalization;

namespace Metrics.Application.Formulas;

internal enum TokenKind { Number, Ref, Plus, Minus, Star, Slash, LParen, RParen, End }

internal readonly record struct Token(TokenKind Kind, int Position, string Text = "", decimal Number = 0);

internal sealed class FormulaSyntaxException(FormulaError error) : Exception(error.Message)
{
    public FormulaError Error { get; } = error;
}

/// <summary>Hand-rolled lexer. Stops at the first lexical error.</summary>
internal static class Tokenizer
{
    public static List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        var i = 0;

        while (i < s.Length)
        {
            var c = s[i];
            var pos = i + 1;

            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < s.Length && char.IsAsciiDigit(s[i + 1])))
            {
                var start = i;
                var seenDot = false;
                while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.'))
                {
                    if (s[i] == '.')
                    {
                        if (seenDot) throw Error(pos, "Invalid number: more than one decimal point.");
                        seenDot = true;
                    }
                    i++;
                }
                var text = s[start..i];
                if (text.EndsWith('.') ||
                    !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
                    throw Error(pos, $"Invalid number '{text}'.");
                tokens.Add(new Token(TokenKind.Number, pos, text, value));
                continue;
            }

            switch (c)
            {
                case '[':
                    var close = s.IndexOf(']', i + 1);
                    if (close < 0) throw Error(pos, "Missing closing ']' for metric reference.");
                    var label = s[(i + 1)..close];
                    if (label.Contains('[')) throw Error(pos, "Metric references cannot be nested.");
                    label = label.Trim();
                    if (label.Length == 0) throw Error(pos, "Empty metric reference '[]'.");
                    tokens.Add(new Token(TokenKind.Ref, pos, label));
                    i = close + 1;
                    continue;
                case '+': tokens.Add(new Token(TokenKind.Plus, pos, "+")); break;
                case '-' or '−': tokens.Add(new Token(TokenKind.Minus, pos, "-")); break;
                case '*' or '×': tokens.Add(new Token(TokenKind.Star, pos, "*")); break;
                case '/' or '÷': tokens.Add(new Token(TokenKind.Slash, pos, "/")); break;
                case '(': tokens.Add(new Token(TokenKind.LParen, pos, "(")); break;
                case ')': tokens.Add(new Token(TokenKind.RParen, pos, ")")); break;
                default: throw Error(pos, $"Unexpected character '{c}'. Wrap metric names in [brackets].");
            }
            i++;
        }

        tokens.Add(new Token(TokenKind.End, s.Length + 1));
        return tokens;
    }

    private static FormulaSyntaxException Error(int position, string message) =>
        new(new FormulaError(FormulaErrorCode.SyntaxError, message, position));
}
