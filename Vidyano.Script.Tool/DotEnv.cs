using System;
using System.Collections.Generic;

namespace Vidyano.Script.Tool;

/// <summary>
/// Minimal <c>.env</c> parser backing the CLI's <c>--env-file</c> flag. Deliberately literal: no quote
/// stripping, no <c>${VAR}</c> expansion, no inline comments — a value is exactly the text after the first
/// <c>=</c> (surrounding whitespace trimmed). Recognizes blank lines, full-line <c>#</c> comments, and an
/// optional leading <c>export </c>. Lines without an <c>=</c> (or with an empty key) are skipped. The
/// parsed pairs feed a composed <see cref="VidyanoScriptOptions.EnvLookup"/> that shadows the process env.
/// <para>An empty value (<c>KEY=</c>) is stored as <c>""</c>, so it shadows — effectively clears — any
/// same-named process variable: the env consumers (<c>{{env:NAME}}</c>, <c>SIGN-IN FROM ENV</c>) treat an
/// empty string as unset, so they fall to a <c>?? fallback</c> or loud-fail rather than reading the process
/// value. Use this to deliberately unset a var for a run.</para>
/// </summary>
public static class DotEnv
{
    /// <summary>Parses <c>.env</c> content into KEY→value pairs. Keys are matched case-sensitively
    /// (conventional dotenv behavior); on a duplicate key the last occurrence wins.</summary>
    public static Dictionary<string, string?> Parse(string content)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(content))
            return result;

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            if (line.StartsWith("export ", StringComparison.Ordinal))
                line = line.Substring("export ".Length).TrimStart();

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue; // no '=' or empty key — not a binding

            var key = line.Substring(0, eq).TrimEnd();
            if (key.Length == 0)
                continue;
            result[key] = line.Substring(eq + 1).Trim();
        }

        return result;
    }
}
