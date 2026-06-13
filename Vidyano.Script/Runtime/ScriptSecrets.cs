using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Vidyano.Script.Parsing;

namespace Vidyano.Script.Runtime;

/// <summary>
/// Keeps plaintext credentials out of persisted .visc and out of aggregated variable dumps. A REPL session
/// where you type <c>SIGN-IN admin / hunter2</c> would otherwise write that password verbatim when you
/// <c>:save</c> the history, and <c>:vars</c> would echo any secret-looking variable. This redacts an inline
/// SIGN-IN password (and secret-named literal assignments) to an <c>{{env:…}}</c> reference — keeping the saved
/// script runnable the blessed way (supply the secret via the environment) instead of baking it into a file
/// that might be committed.
/// </summary>
public static class ScriptSecrets
{
    // Substring match on a variable/credential name. Deliberately broad: a false positive only hides a value
    // from a dump or routes it through env on save — both safe — while a miss could leak a real secret.
    private static readonly Regex SecretName = new(
        @"(password|passwd|pwd|secret|token|api[_-]?key|apikey|credential|auth)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The default environment variable an inline SIGN-IN password is redirected to — the same name
    /// <c>SIGN-IN FROM ENV</c> reads, so a redacted script runs with the same setup.</summary>
    public const string SignInPasswordEnv = "VIDYANO_PASSWORD";

    /// <summary>True when a variable/credential name looks secret and its value should be hidden from dumps and
    /// kept out of saved scripts.</summary>
    public static bool IsSecretName(string? name) => !string.IsNullOrEmpty(name) && SecretName.IsMatch(name!);

    /// <summary>
    /// Returns <paramref name="line"/> with any inline secret replaced by an <c>{{env:…}}</c> reference, and
    /// whether anything changed. Two shapes are redacted: a <c>SIGN-IN … / &lt;literal&gt;</c> password becomes
    /// <c>{{env:VIDYANO_PASSWORD}}</c>, and a <c>@&lt;secret-name&gt; = &lt;literal&gt;</c> assignment becomes
    /// <c>{{env:&lt;NAME&gt;}}</c>. A password/value that is already an interpolation (<c>{{…}}</c>) or
    /// <c>SIGN-IN FROM ENV</c> is left untouched. Other lines pass through unchanged.
    /// </summary>
    public static (string Line, bool Changed) RedactLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return (line, false);

        var tokens = new Lexer(line, "<redact>").Tokenize();
        if (tokens.Count == 0) return (line, false);

        // SIGN-IN … / <inline password> — replace the password token (everything from its start column up to
        // the next token, e.g. a trailing LANGUAGE clause, or end of line).
        if (tokens[0].Kind == TokenKind.Identifier &&
            string.Equals(tokens[0].Lexeme, "SIGN-IN", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Kind != TokenKind.Slash) continue;
                if (i + 1 < tokens.Count && IsInlineLiteral(tokens[i + 1].Kind))
                    return (ReplaceTokenSpan(line, tokens, i + 1, $"{{{{env:{SignInPasswordEnv}}}}}"), true);
                break; // a Slash with no literal after it (e.g. {{env:…}}) — nothing to redact
            }
            return (line, false);
        }

        // @<secret-name> = <inline literal>  →  @<secret-name> = {{env:<NAME>}}
        if (tokens.Count >= 3 && tokens[0].Kind == TokenKind.At && IsSecretName(tokens[0].Lexeme) &&
            tokens[1].Kind == TokenKind.Equals && IsInlineLiteral(tokens[2].Kind))
        {
            return (ReplaceTokenSpan(line, tokens, 2, $"{{{{env:{tokens[0].Lexeme.ToUpperInvariant()}}}}}"), true);
        }

        return (line, false);
    }

    // A value baked literally into the source (so it would persist), as opposed to an interpolation/handle
    // that resolves elsewhere.
    private static bool IsInlineLiteral(TokenKind kind) =>
        kind is TokenKind.String or TokenKind.Identifier or TokenKind.Integer or TokenKind.Number or TokenKind.Literal;

    // Replaces the source span of token <paramref name="index"/> — [its start column, the next token's start
    // column) with trailing whitespace trimmed. Using token *boundaries* (not Lexeme length) is robust to the
    // quote characters and escape decoding that make a string token's Lexeme shorter than its source span.
    private static string ReplaceTokenSpan(string line, IReadOnlyList<Token> tokens, int index, string replacement)
    {
        var start = tokens[index].Location.Column - 1;
        var end = index + 1 < tokens.Count ? tokens[index + 1].Location.Column - 1 : line.Length;
        if (start < 0) start = 0;
        if (end > line.Length) end = line.Length;
        if (end < start) end = start;
        while (end > start && char.IsWhiteSpace(line[end - 1])) end--;
        return line.Substring(0, start) + replacement + line.Substring(end);
    }
}
