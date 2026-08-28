using System.Globalization;

namespace Pane.Plugins.Calculator;

public static class Expression
{
    public static bool TryEval(string input, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;
        try
        {
            var tokens = Tokenize(input);
            if (tokens.Count == 0) return false;
            var rpn = ToRpn(tokens);
            result = EvalRpn(rpn);
            return !double.IsNaN(result) && !double.IsInfinity(result);
        }
        catch { return false; }
    }

    static List<string> Tokenize(string s)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (char.IsDigit(c) || c == '.')
            {
                int start = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                tokens.Add(s[start..i]);
            }
            else if ("+-*/()".Contains(c)) { tokens.Add(c.ToString()); i++; }
            else throw new FormatException($"bad char {c}");
        }
        return tokens;
    }

    // Precedence: binary +/- = 1, binary */÷ = 2, unary u-/u+ = 3
    static int Prec(string op) => op is "u-" or "u+" ? 3 : op is "+" or "-" ? 1 : 2;

    // Unary ops are right-associative; binary ops are left-associative.
    static bool IsRightAssoc(string op) => op is "u-" or "u+";

    static List<string> ToRpn(List<string> tokens)
    {
        var output = new List<string>();
        var ops = new Stack<string>();
        for (int idx = 0; idx < tokens.Count; idx++)
        {
            var t = tokens[idx];
            if (double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) output.Add(t);
            else if (t == "(") ops.Push(t);
            else if (t == ")")
            {
                while (ops.Count > 0 && ops.Peek() != "(") output.Add(ops.Pop());
                if (ops.Count == 0) throw new FormatException("mismatched paren");
                ops.Pop();
            }
            else // operator
            {
                // unary minus/plus: a leading - or + or after ( or another operator
                bool unary = t is "-" or "+" &&
                    (idx == 0 || tokens[idx - 1] is "(" or "+" or "-" or "*" or "/");
                string op = unary ? (t == "-" ? "u-" : "u+") : t;
                // Drain: for left-assoc drain on >= prec; for right-assoc drain only on strictly >
                while (ops.Count > 0 && ops.Peek() != "(" &&
                    (Prec(ops.Peek()) > Prec(op) || (Prec(ops.Peek()) == Prec(op) && !IsRightAssoc(op))))
                    output.Add(ops.Pop());
                ops.Push(op);
            }
        }
        while (ops.Count > 0)
        {
            var op = ops.Pop();
            if (op is "(" or ")") throw new FormatException("mismatched paren");
            output.Add(op);
        }
        return output;
    }

    static double EvalRpn(List<string> rpn)
    {
        var st = new Stack<double>();
        foreach (var t in rpn)
        {
            if (t == "u-") { if (st.Count < 1) throw new FormatException("insufficient operands"); st.Push(-st.Pop()); }
            else if (t == "u+") { if (st.Count < 1) throw new FormatException("insufficient operands"); /* no-op */ }
            else if (double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var num)) st.Push(num);
            else
            {
                if (st.Count < 2) throw new FormatException("insufficient operands");
                var b = st.Pop(); var a = st.Pop();
                st.Push(t switch { "+" => a + b, "-" => a - b, "*" => a * b, "/" => a / b, _ => throw new FormatException() });
            }
        }
        if (st.Count != 1) throw new FormatException("leftover operands");
        return st.Pop();
    }
}
