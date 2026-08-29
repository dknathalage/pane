namespace Pane.Abstractions;

public interface IFuzzyMatcher
{
    bool TryMatch(string query, string target, out double score, out IReadOnlyList<int> positions);
}

public sealed class FuzzyMatcher : IFuzzyMatcher
{
    const double BaseHit = 1.0;
    const double ConsecutiveBonus = 3.0;
    const double BoundaryBonus = 4.0;
    const double StartBonus = 2.0;
    const double GapPenalty = 0.3;

    public bool TryMatch(string query, string target, out double score, out IReadOnlyList<int> positions)
    {
        score = 0;
        positions = Array.Empty<int>();
        if (string.IsNullOrEmpty(query)) return true;
        if (string.IsNullOrEmpty(target)) return false;

        // A query containing wildcards is matched as an anchored, case-insensitive
        // glob against the whole target (e.g. "*.pdf", "report?.md") rather than a
        // fuzzy subsequence.
        if (query.IndexOfAny(Wildcards) >= 0)
            return TryGlob(query, target, out score);

        var q = query.ToLowerInvariant();
        var t = target.ToLowerInvariant();
        var matched = new List<int>(q.Length);

        int ti = 0;
        double total = 0;
        int prevMatch = -2;
        for (int qi = 0; qi < q.Length; qi++)
        {
            var start = ti;
            while (ti < t.Length && t[ti] != q[qi]) ti++;
            if (ti >= t.Length) { score = 0; positions = Array.Empty<int>(); return false; }

            double s = BaseHit;
            if (ti == 0) s += StartBonus;
            if (ti == prevMatch + 1) s += ConsecutiveBonus;
            if (IsBoundary(target, ti)) s += BoundaryBonus;
            s -= (ti - start) * GapPenalty;              // penalize skipped chars

            total += s;
            matched.Add(ti);
            prevMatch = ti;
            ti++;
        }

        score = Math.Max(total, 0.01);                   // any real match scores > 0
        positions = matched;
        return true;
    }

    static readonly char[] Wildcards = { '*', '?' };

    // Anchored glob match: * → any run, ? → exactly one char. Score reflects how
    // many literal (non-wildcard) chars the pattern pins down, so more specific
    // patterns rank above looser ones.
    static bool TryGlob(string query, string target, out double score)
    {
        score = 0;
        var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(query)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                target, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return false;

        var literals = query.Count(c => c != '*' && c != '?');
        score = Math.Max(literals, 0.01);
        return true;
    }

    static bool IsBoundary(string original, int i)
    {
        if (i == 0) return true;
        char prev = original[i - 1];
        if (prev is ' ' or '-' or '_' or '/' or '.') return true;
        return char.IsUpper(original[i]) && char.IsLower(prev);
    }
}
