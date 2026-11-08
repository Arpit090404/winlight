using System.Globalization;
using System.Text;

namespace WinLight.Core;

public sealed class CalculatorProvider : IResultProvider
{
    private static readonly IReadOnlyList<SearchResult> Empty = Array.Empty<SearchResult>();
    public string Id => "calculator";

    public ValueTask<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        ReadOnlySpan<char> text = query.AsSpan().Trim();
        if (ct.IsCancellationRequested || text.IsEmpty || text.Length > 256 || !CouldBeExpression(text))
            return ValueTask.FromResult(Empty);

        bool forced = text[0] == '=';
        if (forced) text = text[1..].Trim();
        if (text.IsEmpty) return ValueTask.FromResult(Empty);

        var parser = new Parser(text);
        if (!parser.TryParse(out decimal value, out bool meaningful) || (!forced && !meaningful))
            return ValueTask.FromResult(Empty);

        string title = Format(value);
        string subtitle = Normalize(text) + " =";
        IReadOnlyList<SearchResult> result =
        [new SearchResult(title, subtitle, null, 1, Id, new ResultAction.CopyText(title))];
        return ValueTask.FromResult(result);
    }

    private static bool CouldBeExpression(ReadOnlySpan<char> text)
    {
        if (text[0] == '=') text = text[1..].TrimStart();
        if (text.IsEmpty) return false;
        char first = text[0];
        if (!(char.IsAsciiDigit(first) || first is '.' or '(' or '+' or '-'))
        {
            if (!IsKnownIdentifierStart(first)) return false;
        }
        for (int i = 0; i < text.Length;)
        {
            if (!char.IsLetter(text[i])) { i++; continue; }
            int start = i;
            while (i < text.Length && char.IsLetter(text[i])) i++;
            ReadOnlySpan<char> word = text[start..i];
            if (!IsKnownWord(word)) return false;
        }
        return true;
    }

    private static bool IsKnownIdentifierStart(char c) =>
        c is 's' or 'S' or 'a' or 'A' or 'r' or 'R' or 'f' or 'F' or 'c' or 'C' or
            'l' or 'L' or 'e' or 'E' or 't' or 'T' or 'p' or 'P' or 'm' or 'M';

    private static bool IsKnownWord(ReadOnlySpan<char> word) =>
        word.Equals("sqrt", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("abs", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("round", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("floor", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("ceil", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("ln", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("log", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("exp", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("sin", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("cos", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("tan", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("pi", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("e", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("mod", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("of", StringComparison.OrdinalIgnoreCase);

    private static string Format(decimal value)
    {
        string rounded = value.ToString("G12", CultureInfo.InvariantCulture);
        decimal ordinary = decimal.Parse(rounded, NumberStyles.Float, CultureInfo.InvariantCulture);
        return ordinary.ToString("0.############################", CultureInfo.InvariantCulture);
    }

    private static string Normalize(ReadOnlySpan<char> text)
    {
        var builder = new StringBuilder(text.Length);
        bool space = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c)) { space = true; continue; }
            if (space && builder.Length > 0) builder.Append(' ');
            builder.Append(c is '×' ? '*' : c is '÷' ? '/' : c);
            space = false;
        }
        return builder.ToString();
    }

    private ref struct Parser(ReadOnlySpan<char> text)
    {
        private readonly ReadOnlySpan<char> _text = text;
        private int _index;
        private int _depth;
        private bool _meaningful;

        public bool TryParse(out decimal value, out bool meaningful)
        {
            value = 0;
            meaningful = false;
            try
            {
                Node node = ParseExpression(0);
                SkipSpace();
                if (_index != _text.Length) return false;
                value = node.Value;
                meaningful = _meaningful;
                return true;
            }
            catch (InvalidExpressionException) { return false; }
            catch (OverflowException) { return false; }
            catch (DivideByZeroException) { return false; }
        }

        private Node ParseExpression(int minPrecedence)
        {
            if (++_depth > 64) throw new InvalidExpressionException();
            try
            {
                Node left = ParsePrefix();
                while (true)
                {
                    SkipSpace();
                    int saved = _index;
                    if (!TryOperator(left.CanFollowNumber, out Operator op, out int precedence))
                        break;
                    if (precedence < minPrecedence) { _index = saved; break; }
                    _meaningful = true;
                    Node right = ParseExpression(precedence + (op == Operator.Power ? 0 : 1));
                    decimal rightValue = right.IsPercent && op is Operator.Add or Operator.Subtract
                        ? left.Value * right.Value : right.Value;
                    left = new Node(Apply(op, left.Value, rightValue), false);
                }
                return left;
            }
            finally { _depth--; }
        }

        private Node ParsePrefix()
        {
            SkipSpace();
            if (Take('+')) { _meaningful = true; return ParseExpression(3); }
            if (Take('-')) { _meaningful = true; Node operand = ParseExpression(3); return new Node(-operand.Value, false); }
            Node node = ParsePrimary();
            while (true)
            {
                SkipSpace();
                if (!Take('%')) break;
                _meaningful = true;
                node = new Node(node.Value / 100m, true);
                SkipSpace();
                if (TakeWord("of"))
                {
                    Node other = ParseExpression(3);
                    node = new Node(node.Value * other.Value, false);
                }
            }
            return node;
        }

        private Node ParsePrimary()
        {
            SkipSpace();
            if (Take('('))
            {
                Node inner = ParseExpression(0);
                SkipSpace();
                if (!Take(')')) throw new InvalidExpressionException();
                return new Node(inner.Value, inner.IsPercent, true);
            }
            if (_index >= _text.Length) throw new InvalidExpressionException();
            if (char.IsAsciiDigit(_text[_index]) || _text[_index] == '.')
                return new Node(ParseNumber(), false);
            if (!char.IsLetter(_text[_index])) throw new InvalidExpressionException();
            ReadOnlySpan<char> word = ReadWord();
            if (word.Equals("pi", StringComparison.OrdinalIgnoreCase))
                return new Node(3.1415926535897932384626433833m, false, true);
            if (word.Equals("e", StringComparison.OrdinalIgnoreCase))
                return new Node(2.7182818284590452353602874714m, false, true);
            if (!IsFunction(word)) throw new InvalidExpressionException();
            _meaningful = true;
            SkipSpace();
            if (!Take('(')) throw new InvalidExpressionException();
            Node argument = ParseExpression(0);
            SkipSpace();
            if (!Take(')')) throw new InvalidExpressionException();
            return new Node(ApplyFunction(word, argument.Value), false, true);
        }

        private decimal ParseNumber()
        {
            int start = _index;
            bool digits = false;
            while (_index < _text.Length && char.IsAsciiDigit(_text[_index])) { _index++; digits = true; }
            if (Take('.'))
                while (_index < _text.Length && char.IsAsciiDigit(_text[_index])) { _index++; digits = true; }
            if (!digits) throw new InvalidExpressionException();
            if (_index < _text.Length && (_text[_index] is 'e' or 'E'))
            {
                int exponent = _index++;
                if (_index < _text.Length && _text[_index] is '+' or '-') _index++;
                int exponentDigits = _index;
                while (_index < _text.Length && char.IsAsciiDigit(_text[_index])) _index++;
                if (exponentDigits == _index) _index = exponent;
            }
            if (!decimal.TryParse(_text[start.._index], NumberStyles.AllowDecimalPoint |
                NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out decimal value))
                throw new InvalidExpressionException();
            return value;
        }

        private bool TryOperator(bool canFollowNumber, out Operator op, out int precedence)
        {
            op = default;
            precedence = -1;
            if (_index >= _text.Length) return false;
            char c = _text[_index];
            switch (c)
            {
                case '+': op = Operator.Add; precedence = 1; _index++; return true;
                case '-': op = Operator.Subtract; precedence = 1; _index++; return true;
                case '*': case '×': op = Operator.Multiply; precedence = 2; _index++; return true;
                case '/': case '÷': op = Operator.Divide; precedence = 2; _index++; return true;
                case '^': op = Operator.Power; precedence = 4; _index++; return true;
            }
            if (TakeWord("mod")) { op = Operator.Modulo; precedence = 2; return true; }
            if (c == '(' || char.IsLetter(c) || (canFollowNumber && (char.IsAsciiDigit(c) || c == '.')))
            {
                op = Operator.Multiply;
                precedence = 2;
                return true;
            }
            return false;
        }

        private static decimal Apply(Operator op, decimal left, decimal right) => op switch
        {
            Operator.Add => left + right,
            Operator.Subtract => left - right,
            Operator.Multiply => left * right,
            Operator.Divide => left / right,
            Operator.Modulo => left % right,
            Operator.Power => FromDouble(Math.Pow((double)left, (double)right)),
            _ => throw new InvalidExpressionException()
        };

        private static bool IsFunction(ReadOnlySpan<char> word) =>
            word.Equals("sqrt", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("abs", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("round", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("floor", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("ceil", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("ln", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("log", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("exp", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("sin", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("cos", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("tan", StringComparison.OrdinalIgnoreCase);

        private static decimal ApplyFunction(ReadOnlySpan<char> word, decimal value)
        {
            if (word.Equals("abs", StringComparison.OrdinalIgnoreCase)) return Math.Abs(value);
            if (word.Equals("round", StringComparison.OrdinalIgnoreCase))
                return Math.Round(value, 0, MidpointRounding.AwayFromZero);
            if (word.Equals("floor", StringComparison.OrdinalIgnoreCase)) return Math.Floor(value);
            if (word.Equals("ceil", StringComparison.OrdinalIgnoreCase)) return Math.Ceiling(value);
            double input = (double)value;
            double result = word.Equals("sqrt", StringComparison.OrdinalIgnoreCase) ? Math.Sqrt(input) :
                word.Equals("ln", StringComparison.OrdinalIgnoreCase) ? Math.Log(input) :
                word.Equals("log", StringComparison.OrdinalIgnoreCase) ? Math.Log10(input) :
                word.Equals("exp", StringComparison.OrdinalIgnoreCase) ? Math.Exp(input) :
                word.Equals("sin", StringComparison.OrdinalIgnoreCase) ? Math.Sin(input) :
                word.Equals("cos", StringComparison.OrdinalIgnoreCase) ? Math.Cos(input) :
                Math.Tan(input);
            return FromDouble(result);
        }

        private static decimal FromDouble(double value)
        {
            if (!double.IsFinite(value) || value > (double)decimal.MaxValue ||
                value < (double)decimal.MinValue) throw new InvalidExpressionException();
            return (decimal)value;
        }

        private ReadOnlySpan<char> ReadWord()
        {
            int start = _index;
            while (_index < _text.Length && char.IsLetter(_text[_index])) _index++;
            return _text[start.._index];
        }

        private bool TakeWord(string word)
        {
            if (!_text[_index..].StartsWith(word, StringComparison.OrdinalIgnoreCase)) return false;
            int end = _index + word.Length;
            if (end < _text.Length && char.IsLetter(_text[end])) return false;
            _index = end;
            return true;
        }

        private bool Take(char c)
        {
            if (_index >= _text.Length || _text[_index] != c) return false;
            _index++;
            return true;
        }

        private void SkipSpace()
        {
            while (_index < _text.Length && char.IsWhiteSpace(_text[_index])) _index++;
        }

        private readonly record struct Node(decimal Value, bool IsPercent, bool CanFollowNumber = false);
        private enum Operator { Add, Subtract, Multiply, Divide, Modulo, Power }
        private sealed class InvalidExpressionException : Exception;
    }
}
