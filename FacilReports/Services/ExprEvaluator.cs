using System.Globalization;
using System.Text;

namespace FacilReports.Services;

/// <summary>
/// Evaluador del subconjunto del lenguaje de expresiones de DevExpress que usan las plantillas:
/// literales 'texto' y números, campos [A], [Documento.A], [Parameters.A], [?A], operadores
/// + - * / = &lt;&gt; &lt; &gt; &lt;= &gt;= And Or Not, y las funciones Len, Iif, Concat, Trim, Upper,
/// Lower, IsNullOrEmpty, ToStr. Lo desconocido lanza NotSupportedException con el texto exacto.
/// </summary>
public sealed class ExprEvaluator
{
    private readonly string _src;
    private int _pos;
    private readonly Func<string, object?> _field;

    private ExprEvaluator(string src, Func<string, object?> field) { _src = src; _field = field; }

    public static object? Evaluate(string expression, Func<string, object?> field)
    {
        var e = new ExprEvaluator(expression, field);
        var v = e.ParseOr();
        e.SkipWs();
        if (e._pos < e._src.Length)
            throw new NotSupportedException($"Expresión no soportada cerca de '{e._src[e._pos..]}' en: {expression}");
        return v;
    }

    public static string EvaluateString(string expression, Func<string, object?> field) => Str(Evaluate(expression, field));
    public static bool EvaluateBool(string expression, Func<string, object?> field) => Truthy(Evaluate(expression, field));

    // ---- valores ------------------------------------------------------------------------
    private static string Str(object? v) => v switch
    {
        null => string.Empty,
        bool b => b ? "True" : "False",
        double d => d.ToString("0.############", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? string.Empty
    };

    private static bool Truthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        double d => d != 0,
        string s => s.Length > 0 && !s.Equals("false", StringComparison.OrdinalIgnoreCase),
        _ => true
    };

    private static bool TryNum(object? v, out double d)
    {
        switch (v)
        {
            case double x: d = x; return true;
            case int i: d = i; return true;
            case long l: d = l; return true;
            case decimal m: d = (double)m; return true;
            case string s: return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
            default: d = 0; return false;
        }
    }

    // ---- gramática ----------------------------------------------------------------------
    private object? ParseOr()
    {
        var left = ParseAnd();
        while (Accept("Or")) { var right = ParseAnd(); left = Truthy(left) || Truthy(right); }
        return left;
    }

    private object? ParseAnd()
    {
        var left = ParseNot();
        while (Accept("And")) { var right = ParseNot(); left = Truthy(left) && Truthy(right); }
        return left;
    }

    private object? ParseNot()
    {
        if (Accept("Not") || AcceptSym("!")) return !Truthy(ParseNot());
        return ParseCmp();
    }

    private object? ParseCmp()
    {
        var left = ParseAdd();
        while (true)
        {
            SkipWs();
            string? op = null;
            foreach (var c in new[] { "<>", "!=", "<=", ">=", "==", "=", "<", ">" })
                if (string.CompareOrdinal(_src, _pos, c, 0, c.Length) == 0) { op = c; break; }
            if (op == null) return left;
            _pos += op.Length;
            var right = ParseAdd();
            int cmp;
            if (TryNum(left, out var a) && TryNum(right, out var b) && left is not null && right is not null) cmp = a.CompareTo(b);
            else cmp = string.Compare(Str(left), Str(right), StringComparison.OrdinalIgnoreCase);
            left = op switch
            {
                "=" or "==" => cmp == 0,
                "<>" or "!=" => cmp != 0,
                "<" => cmp < 0,
                ">" => cmp > 0,
                "<=" => cmp <= 0,
                _ => cmp >= 0
            };
        }
    }

    private object? ParseAdd()
    {
        var left = ParseMul();
        while (true)
        {
            SkipWs();
            if (Peek() == '+')
            {
                _pos++; var right = ParseMul();
                // texto si alguno de los lados es texto no numérico; suma si ambos son números reales
                left = left is double && right is double ? (double)left + (double)right : Str(left) + Str(right);
            }
            else if (Peek() == '-')
            {
                _pos++; var right = ParseMul();
                left = TryNum(left, out var a) && TryNum(right, out var b) ? a - b : throw new NotSupportedException("Resta con operandos no numéricos.");
            }
            else return left;
        }
    }

    private object? ParseMul()
    {
        var left = ParseUnary();
        while (true)
        {
            SkipWs();
            if (Peek() == '*') { _pos++; left = Num(left) * Num(ParseUnary()); }
            else if (Peek() == '/') { _pos++; var d = Num(ParseUnary()); left = d == 0 ? 0d : Num(left) / d; }
            else return left;
        }
    }

    private static double Num(object? v) => TryNum(v, out var d) ? d : 0;

    private object? ParseUnary()
    {
        SkipWs();
        if (Peek() == '-') { _pos++; return -Num(ParseUnary()); }
        return ParsePrimary();
    }

    private object? ParsePrimary()
    {
        SkipWs();
        var c = Peek();
        if (c == '(') { _pos++; var v = ParseOr(); Expect(')'); return v; }
        if (c == '\'') return ReadString();
        if (c == '[') return ReadField();
        if (c == '?') { _pos++; return _field(ReadIdent()); }
        if (char.IsDigit(c) || c == '.') return ReadNumber();
        if (char.IsLetter(c) || c == '_')
        {
            var id = ReadIdent();
            SkipWs();
            if (Peek() == '(') { _pos++; return CallFunction(id); }
            if (id.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (id.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            if (id.Equals("null", StringComparison.OrdinalIgnoreCase)) return null;
            throw new NotSupportedException($"Identificador no soportado '{id}' en: {_src}");
        }
        throw new NotSupportedException($"Expresión no soportada en posición {_pos}: {_src}");
    }

    private object? CallFunction(string name)
    {
        var args = new List<Func<object?>>();
        // Los argumentos se evalúan perezosamente para que Iif no evalúe la rama descartada.
        var argStarts = new List<int>();
        SkipWs();
        if (Peek() == ')') { _pos++; }
        else
        {
            while (true)
            {
                argStarts.Add(_pos);
                var captured = _pos;
                args.Add(() => { var save = _pos; _pos = captured; var v = ParseOr(); _pos = save; return v; });
                SkipArg();
                SkipWs();
                if (Peek() == ',') { _pos++; continue; }
                Expect(')');
                break;
            }
        }
        switch (name.ToLowerInvariant())
        {
            case "len": return (double)Str(args.Count > 0 ? args[0]() : null).Length;
            case "iif":
                for (var i = 0; i + 1 < args.Count; i += 2)
                    if (Truthy(args[i]())) return args[i + 1]();
                return args.Count % 2 == 1 ? args[^1]() : null;
            case "concat": return string.Concat(args.Select(a => Str(a())));
            case "trim": return Str(args[0]()).Trim();
            case "upper": return Str(args[0]()).ToUpperInvariant();
            case "lower": return Str(args[0]()).ToLowerInvariant();
            case "isnullorempty": return string.IsNullOrEmpty(Str(args[0]()));
            case "tostr": return Str(args[0]());
            default: throw new NotSupportedException($"Función '{name}' no soportada en: {_src}");
        }
    }

    // Avanza _pos por encima de un argumento sin evaluarlo (respeta paréntesis, comillas y corchetes).
    private void SkipArg()
    {
        var depth = 0;
        while (_pos < _src.Length)
        {
            var c = _src[_pos];
            if (c == '\'') { ReadString(); continue; }
            if (c == '[') { while (_pos < _src.Length && _src[_pos] != ']') _pos++; _pos++; continue; }
            if (c == '(') depth++;
            else if (c == ')') { if (depth == 0) return; depth--; }
            else if (c == ',' && depth == 0) return;
            _pos++;
        }
    }

    // ---- léxico -------------------------------------------------------------------------
    private char Peek() => _pos < _src.Length ? _src[_pos] : '\0';
    private void SkipWs() { while (_pos < _src.Length && char.IsWhiteSpace(_src[_pos])) _pos++; }
    private void Expect(char c) { SkipWs(); if (Peek() != c) throw new NotSupportedException($"Se esperaba '{c}' en: {_src}"); _pos++; }

    private bool AcceptSym(string s)
    {
        SkipWs();
        if (string.CompareOrdinal(_src, _pos, s, 0, s.Length) == 0 && Peek() == s[0] && (_pos + 1 >= _src.Length || _src[_pos + 1] != '='))
        { _pos += s.Length; return true; }
        return false;
    }

    private bool Accept(string word)
    {
        SkipWs();
        if (string.Compare(_src, _pos, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0)
        {
            var end = _pos + word.Length;
            if (end >= _src.Length || !(char.IsLetterOrDigit(_src[end]) || _src[end] == '_'))
            { _pos = end; return true; }
        }
        return false;
    }

    private string ReadIdent()
    {
        var s = _pos;
        while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_' || _src[_pos] == '.')) _pos++;
        return _src[s.._pos];
    }

    private string ReadString()
    {
        var sb = new StringBuilder();
        _pos++; // '
        while (_pos < _src.Length)
        {
            var c = _src[_pos];
            if (c == '\'')
            {
                if (_pos + 1 < _src.Length && _src[_pos + 1] == '\'') { sb.Append('\''); _pos += 2; continue; }
                _pos++; return sb.ToString();
            }
            sb.Append(c); _pos++;
        }
        throw new NotSupportedException($"Texto sin cerrar en: {_src}");
    }

    private object? ReadField()
    {
        var end = _src.IndexOf(']', _pos);
        if (end < 0) throw new NotSupportedException($"Campo sin cerrar en: {_src}");
        var name = _src[(_pos + 1)..end].Trim();
        _pos = end + 1;
        if (name.StartsWith('?')) name = name[1..];
        return _field(name);
    }

    private object ReadNumber()
    {
        var s = _pos;
        while (_pos < _src.Length && (char.IsDigit(_src[_pos]) || _src[_pos] == '.')) _pos++;
        return double.Parse(_src[s.._pos], CultureInfo.InvariantCulture);
    }
}
