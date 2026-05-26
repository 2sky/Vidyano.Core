using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidyano.Script.Diagnostics;

/// <summary>
/// Picks plausible alternatives for a misspelled name. Pure function — no allocation surprises,
/// safe to call on every parse/runtime error.
/// </summary>
/// <remarks>
/// Threshold rule of thumb: a candidate qualifies if its Levenshtein distance from the input is
/// at most <c>max(2, len/3)</c>. We rank by distance, then by length parity, then by case-insensitive
/// alphabetical order to keep suggestions stable across runs.
/// </remarks>
public static class Suggester
{
    /// <summary>Returns up to <paramref name="max"/> plausible candidates, closest first.</summary>
    public static IReadOnlyList<string> Suggest(string input, IEnumerable<string> candidates, int max = 3)
    {
        if (string.IsNullOrEmpty(input) || candidates == null)
            return Array.Empty<string>();

        var threshold = Math.Max(2, input.Length / 3);

        var scored = new List<(string candidate, int distance)>();
        foreach (var c in candidates)
        {
            if (string.IsNullOrEmpty(c))
                continue;

            // Equal-ignore-case is a hit but distance 0 still wins; subsequent ranking handles it.
            var d = Levenshtein(input, c, ignoreCase: true);
            if (d <= threshold)
                scored.Add((c, d));
        }

        return scored
            .OrderBy(x => x.distance)
            .ThenBy(x => Math.Abs(x.candidate.Length - input.Length))
            .ThenBy(x => x.candidate, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(x => x.candidate)
            .ToList();
    }

    /// <summary>
    /// Formats a "did you mean" hint suitable for the <see cref="Diagnostic.Hint"/> field.
    /// Returns <c>null</c> if there are no candidates worth suggesting.
    /// </summary>
    public static string? Hint(string input, IEnumerable<string> candidates, string kindLabel = "name")
    {
        var picks = Suggest(input, candidates);
        if (picks.Count == 0)
            return null;
        if (picks.Count == 1)
            return $"Did you mean '{picks[0]}'?";
        return $"Did you mean one of: {string.Join(", ", picks.Select(p => $"'{p}'"))}?";
    }

    private static int Levenshtein(string a, string b, bool ignoreCase)
    {
        if (ignoreCase)
        {
            a = a.ToLowerInvariant();
            b = b.ToLowerInvariant();
        }

        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        // Two-row DP. Cheap enough that we don't bother with the early-exit "abs(len diff) > threshold"
        // optimization — names are short and candidate lists are small (attributes, actions, menu items).
        var prev = new int[b.Length + 1];
        var cur  = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }

        return prev[b.Length];
    }
}
