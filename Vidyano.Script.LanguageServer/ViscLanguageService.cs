using System.Collections.Concurrent;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Vidyano.Script;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Parsing;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Vidyano.Script.LanguageServer;

/// <summary>
/// The pure, transport-free brain of the .visc language server. It owns document state, runs the
/// existing lex+parse pipeline on every change, and answers hover — naming no JSON-RPC or stdio type on
/// its hot path. Diagnostics leave through the single <see cref="IDiagnosticSink"/> port, so the whole
/// open→publish round-trip is observable in a unit test without a real LSP client.
/// </summary>
public sealed class ViscLanguageService
{
    private readonly IDiagnosticSink _sink;
    private readonly ConcurrentDictionary<string, string> _documents = new(StringComparer.Ordinal);

    public ViscLanguageService(IDiagnosticSink sink) => _sink = sink;

    /// <summary>Records a freshly opened document and publishes its diagnostics.</summary>
    public Task DidOpenAsync(string uri, string text, CancellationToken ct = default) =>
        AnalyzeAndPublishAsync(uri, text, ct);

    /// <summary>Replaces a document's text (full-document sync) and republishes.</summary>
    public Task DidChangeAsync(string uri, string text, CancellationToken ct = default) =>
        AnalyzeAndPublishAsync(uri, text, ct);

    /// <summary>Forgets a closed document and clears its diagnostics from the editor.</summary>
    public Task DidCloseAsync(string uri, CancellationToken ct = default)
    {
        _documents.TryRemove(uri, out _);
        return _sink.PublishAsync(uri, [], ct);
    }

    /// <summary>Returns verb documentation for the word under <paramref name="position"/>, or
    /// <c>null</c> when the cursor is not on a known verb (whitespace, an argument, an unknown lexeme).</summary>
    public Hover? Hover(string uri, Position position)
    {
        if (!_documents.TryGetValue(uri, out var text))
            return null;

        var word = WordAt(text, position, out var range);
        if (word is null || !VerbCatalog.TryGet(word, out var info))
            return null;

        return new Hover
        {
            Range = range,
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = RenderHover(info),
            }),
        };
    }

    // === Semantic tokens ===
    // The standard LSP token-type names this server emits, in frozen legend order. Modifiers are unused in
    // v1. Computed once: the field initializers below resolve their indices through IndexOf, so a name typo
    // here trips ArgumentException at type load (startup), never as a silent miscoloring at render time.
    private static readonly SemanticTokensLegendSpec _legend = new(
        ["keyword", "type", "string", "number", "comment", "operator", "variable", "macro", "regexp"],
        []);

    private static readonly int _keyword = _legend.IndexOf("keyword");
    private static readonly int _type = _legend.IndexOf("type");
    private static readonly int _string = _legend.IndexOf("string");
    private static readonly int _number = _legend.IndexOf("number");
    private static readonly int _comment = _legend.IndexOf("comment");
    private static readonly int _variable = _legend.IndexOf("variable");
    private static readonly int _macro = _legend.IndexOf("macro");

    /// <summary>The shared semantic-tokens legend. Static so <c>ViscLspServer</c> registers from the same
    /// instance the producer encodes against — the legend index of every emitted span resolves identically
    /// on both ends of the JSON-RPC contract.</summary>
    public static SemanticTokensLegendSpec Legend => _legend;

    /// <summary>All semantic-token spans for a tracked document, ordered for an LSP builder: ascending by
    /// <c>(Line, StartChar)</c> and non-overlapping. An unknown/closed uri or empty document yields an empty
    /// list — never null, never throwing. Full-document only (v1; no range/delta).</summary>
    /// <remarks>
    /// Hides the gap between what the lexer keeps and what an editor must color: verbs/sub-keywords/type-words
    /// all arrive as bare identifiers (re-classified via <see cref="VerbCatalog"/> + <see cref="KeywordCatalog"/>);
    /// bare <c>#…EOL</c> comments are dropped by the lexer (re-scanned from the source here); a string literal's
    /// <c>{{hole}}</c>s are split into string-run / hole-punctuation / hole-content sub-spans; and all lengths
    /// + UTF-16/astral/CRLF widening route through the same <see cref="ToLsp"/> math hover uses.
    /// </remarks>
    public IReadOnlyList<SemanticToken> SemanticTokens(string uri)
    {
        if (!_documents.TryGetValue(uri, out var text) || text.Length == 0)
            return [];

        var tokens = new Lexer(text, uri).Tokenize();
        var spans = new List<SemanticToken>(tokens.Count);

        foreach (var t in tokens)
        {
            switch (t.Kind)
            {
                case TokenKind.Identifier:
                    AddSimple(spans, text, t.Location, CodePointCount(t.Lexeme), Classify(t.Lexeme));
                    break;
                case TokenKind.Number:
                case TokenKind.Integer:
                    AddSimple(spans, text, t.Location, CodePointCount(t.Lexeme), _number);
                    break;
                case TokenKind.At:
                    // The lexeme excludes the leading '@'; the location points at it, so the source span is one
                    // wider. The whole @handle colors as a variable.
                    AddSimple(spans, text, t.Location, CodePointCount(t.Lexeme) + 1, _variable);
                    break;
                case TokenKind.String:
                    AddStringSpans(spans, text, t.Location);
                    break;
                case TokenKind.Interp:
                    AddInterpSpans(spans, text, t.Location);
                    break;
                case TokenKind.StepHeader:
                    AddLineComment(spans, text, t.Location);
                    break;
                default:
                    break; // operators/punctuation/literals/newlines carry no semantic color in v1
            }
        }

        AddDroppedComments(spans, text, tokens);

        spans.Sort(static (a, b) =>
            a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.StartChar.CompareTo(b.StartChar));
        return spans;
    }

    // Maps a (re-classified) identifier to its legend index. Sub-keywords share the keyword color with verbs
    // (both are reserved control words); type-words map to `type`; everything else is a free identifier —
    // an attribute, action, menu segment, or query/PO name — which colors as `variable` (the reference-like
    // role those bare names play), matching the @handle variables.
    private static int Classify(string lexeme) =>
        KeywordCatalog.Classify(lexeme) switch
        {
            SemanticCategory.Verb => _keyword,
            SemanticCategory.SubKeyword => _keyword,
            SemanticCategory.TypeWord => _type,
            _ => _variable,
        };

    // Adds one span for a token whose source occupies `unicodeChars` Unicode characters starting at `loc`.
    // Length is re-derived through ToLsp at both endpoints so astral widening / CRLF trimming are reused, not
    // reinvented. A negative typeIndex (unclassified identifier) emits nothing.
    private static void AddSimple(List<SemanticToken> spans, string text, SourceLocation loc, int unicodeChars, int typeIndex)
    {
        if (typeIndex < 0 || unicodeChars <= 0)
            return;
        var (line, startChar) = ToLsp(loc, text);
        var (_, endChar) = ToLsp(loc with { Column = loc.Column + unicodeChars }, text);
        if (endChar > startChar)
            spans.Add(new SemanticToken(line, startChar, endChar - startChar, typeIndex, 0));
    }

    // Splits a string literal at `loc` into ordered sub-spans: string runs (string color) interleaved with
    // {{hole}}s, each hole emitting its `{{`/`}}` punctuation and inner content as macro spans (mirroring the
    // TextMate nested grammar). Strings never span lines, so all work is within one line's text in UTF-16
    // units — which, past the ToLsp start, are already LSP characters.
    private void AddStringSpans(List<SemanticToken> spans, string text, SourceLocation loc)
    {
        var (line, openChar) = ToLsp(loc, text);
        var lineText = LineText(text, line);
        if (openChar >= lineText.Length || lineText[openChar] != '"')
            return;

        var i = openChar + 1;            // past the opening quote
        var runStart = openChar;         // the opening quote belongs to the leading string run
        while (i < lineText.Length && lineText[i] != '"')
        {
            if (lineText[i] == '\\' && i + 1 < lineText.Length)
            {
                i += 2; // an escape (incl. \{ / \}) is part of the run, never a hole opener
                continue;
            }
            if (lineText[i] == '{' && i + 1 < lineText.Length && lineText[i + 1] == '{')
            {
                AddSpan(spans, line, runStart, i, _string);          // string run up to the hole
                var holeOpen = i;
                i += 2;
                var contentStart = i;
                while (i < lineText.Length && lineText[i] != '"' &&
                       !(lineText[i] == '}' && i + 1 < lineText.Length && lineText[i + 1] == '}'))
                    i++;
                AddSpan(spans, line, holeOpen, contentStart, _macro);   // the `{{`
                AddSpan(spans, line, contentStart, i, _macro);          // hole content
                if (i < lineText.Length && lineText[i] == '}')
                {
                    AddSpan(spans, line, i, i + 2, _macro);             // the `}}`
                    i += 2;
                }
                runStart = i;
                continue;
            }
            i++;
        }
        var runEnd = i < lineText.Length ? i + 1 : i; // include the closing quote when present
        AddSpan(spans, line, runStart, runEnd, _string);
    }

    // Colors a standalone {{hole}} (TokenKind.Interp) in a value position, e.g. `SET Name = {{@uuid}}`, the
    // same way AddStringSpans colors a string-embedded hole — `{{`/content/`}}` as macro — so the identical
    // construct reads identically whether bare or nested. Mirrors the lexer's ReadInterp, whose content runs to
    // `}}` or EOL: unlike a string-embedded hole, a bare hole may carry a quoted fallback (`{{env:X ?? "y"}}`),
    // so the scan must NOT stop at `"`. Interps never span lines, so all work is within one line's UTF-16 units.
    private void AddInterpSpans(List<SemanticToken> spans, string text, SourceLocation loc)
    {
        var (line, openChar) = ToLsp(loc, text);
        var lineText = LineText(text, line);
        if (openChar + 1 >= lineText.Length || lineText[openChar] != '{' || lineText[openChar + 1] != '{')
            return;

        var i = openChar + 2;
        var contentStart = i;
        while (i < lineText.Length && !(lineText[i] == '}' && i + 1 < lineText.Length && lineText[i + 1] == '}'))
            i++;
        AddSpan(spans, line, openChar, contentStart, _macro);   // the `{{`
        AddSpan(spans, line, contentStart, i, _macro);          // hole content
        if (i < lineText.Length && lineText[i] == '}')
            AddSpan(spans, line, i, i + 2, _macro);             // the `}}`
    }

    // Re-derives bare `#…EOL` comments the lexer drops. A `#` only opens a comment outside a string, outside a
    // standalone `{{…}}` hole, and outside a `###` step header, so the scan replays just enough of the lexer's
    // outer state (string skipping with lexer-faithful escapes, hole skipping, the line-start `###` guard) to
    // land on the same `#`s — then colors from there to EOL.
    private void AddDroppedComments(List<SemanticToken> spans, string text, List<Token> tokens)
    {
        // StepHeader spans are already emitted; collect their start lines so this scan doesn't double-color the
        // `###` line as a `#` comment.
        var stepHeaderLines = new HashSet<int>();
        foreach (var t in tokens)
            if (t.Kind == TokenKind.StepHeader)
                stepHeaderLines.Add(ToLsp(t.Location, text).Line);

        var line = 0;
        var col = 0;
        var inString = false;
        var inHole = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n')
            {
                line++;
                col = 0;
                inString = false;
                inHole = false;
                continue;
            }
            if (inHole)
            {
                // A standalone {{…}} hole runs to `}}` or EOL (Lexer.ReadInterp); a `#` inside it is hole
                // content, not a comment. A `"` inside it is content too, so we never enter string state here.
                if (c == '}' && i + 1 < text.Length && text[i + 1] == '}') { inHole = false; i++; col += 2; continue; }
                col++;
                continue;
            }
            if (inString)
            {
                // Mirror the lexer: an escape consumes the next char unconditionally — a `\` before a newline
                // continues the string onto the next line (Lexer.ReadString) rather than ending it.
                if (c == '\\')
                {
                    if (i + 1 < text.Length)
                    {
                        if (text[i + 1] == '\n') { i++; line++; col = 0; }
                        else if (text[i + 1] == '\r' && i + 2 < text.Length && text[i + 2] == '\n') { i += 2; line++; col = 0; }
                        else { i++; col += 2; }
                    }
                    else
                        col++;
                    continue;
                }
                if (c == '"') inString = false;
                col++;
                continue;
            }
            if (c == '"') { inString = true; col++; continue; }
            if (c == '{' && i + 1 < text.Length && text[i + 1] == '{') { inHole = true; i++; col += 2; continue; }
            if (c == '#' && !stepHeaderLines.Contains(line))
            {
                var lineText = LineText(text, line);
                AddSpan(spans, line, col, lineText.Length, _comment);
                // Skip to EOL; the rest of the line is the comment.
                while (i < text.Length && text[i] != '\n') i++;
                line++;
                col = 0;
                continue;
            }
            col++;
        }
    }

    // Colors a whole line (e.g. a ### step header) as a comment from the token's start to EOL.
    private void AddLineComment(List<SemanticToken> spans, string text, SourceLocation loc)
    {
        var (line, startChar) = ToLsp(loc, text);
        var lineText = LineText(text, line);
        AddSpan(spans, line, startChar, lineText.Length, _comment);
    }

    // Adds a [start, end) span in already-computed UTF-16 line coordinates. Empty spans are skipped.
    private static void AddSpan(List<SemanticToken> spans, int line, int startChar, int endChar, int typeIndex)
    {
        if (endChar > startChar)
            spans.Add(new SemanticToken(line, startChar, endChar - startChar, typeIndex, 0));
    }

    // Counts Unicode code points (a surrogate pair is one), so token lengths measured in source characters
    // feed ToLsp's char-column arithmetic correctly.
    private static int CodePointCount(string s)
    {
        var count = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                i++;
            count++;
        }
        return count;
    }

    private async Task AnalyzeAndPublishAsync(string uri, string text, CancellationToken ct)
    {
        _documents[uri] = text;
        var diagnostics = VidyanoScript.Lint(text, uri);
        var mapped = new List<LspDiagnostic>(diagnostics.Count);
        foreach (var d in diagnostics)
            mapped.Add(DiagnosticMapper.ToLsp(d, text));
        await _sink.PublishAsync(uri, mapped, ct).ConfigureAwait(false);
    }

    private static string RenderHover(VerbInfo info)
    {
        var doc = info.MarkdownDoc ?? info.Summary;
        var examples = info.Examples.Count > 0
            ? "\n\n```visc\n" + string.Join("\n", info.Examples) + "\n```"
            : string.Empty;
        return $"**{info.Name}**\n\n```\n{info.Syntax}\n```\n\n{doc}{examples}";
    }

    // === Coordinate translation — exposed static so the math is unit-testable without a service. ===
    // Tokens carry only a start SourceLocation (no length), so an LSP Range can't be read off a token;
    // it is re-derived from the document text here. This is the deliberate "tokens have no length" leak.

    /// <summary>Maps a 1-based engine location to a 0-based LSP (line, UTF-16 character) point. The engine
    /// column counts Unicode characters; LSP counts UTF-16 code units, so astral characters before the
    /// column widen it. <see cref="SourceLocation.Unknown"/> (line 0) collapses to <c>(0, 0)</c>.</summary>
    public static (int Line, int Char) ToLsp(SourceLocation loc, string text)
    {
        if (loc.Line <= 0)
            return (0, 0);

        var lspLine = loc.Line - 1;
        var lineText = LineText(text, lspLine);

        // Walk `Column - 1` Unicode characters into the line, summing UTF-16 code units.
        var targetChars = Math.Max(0, loc.Column - 1);
        var charUnits = 0;
        var consumed = 0;
        while (consumed < targetChars && charUnits < lineText.Length)
        {
            charUnits += char.IsHighSurrogate(lineText[charUnits]) && charUnits + 1 < lineText.Length ? 2 : 1;
            consumed++;
        }
        return (lspLine, charUnits);
    }

    /// <summary>Widens a point location into the <see cref="Range"/> of the word under it (verbs and
    /// hyphenated verbs are one word). When the point is not on an identifier, returns a zero-width range
    /// at the point. <see cref="SourceLocation.Unknown"/> collapses to a zero-width range at <c>(0, 0)</c>.</summary>
    public static Range RangeAt(SourceLocation loc, string text)
    {
        // The Unknown sentinel has no place in the document; collapse to a zero-width range at the
        // origin rather than letting the (0,0) fallback expand into whatever word starts the file.
        if (loc.Line <= 0)
            return new Range(new Position(0, 0), new Position(0, 0));

        var (line, ch) = ToLsp(loc, text);
        var lineText = LineText(text, line);

        if (ch >= lineText.Length || !IsIdentStart(lineText[ch]))
            return new Range(new Position(line, ch), new Position(line, ch));

        var start = ch;
        while (start > 0 && IsIdentCont(lineText[start - 1]))
            start--;
        var end = ch;
        while (end < lineText.Length && IsIdentCont(lineText[end]))
            end++;
        return new Range(new Position(line, start), new Position(line, end));
    }

    private static string? WordAt(string text, Position position, out Range range)
    {
        var lineText = LineText(text, position.Line);
        var ch = position.Character;
        range = new Range(position, position);

        // Hover only when the cursor sits on an identifier character. Whitespace, punctuation, and the
        // EOL position resolve to no word — so a space between tokens never hovers the preceding verb.
        if (ch < 0 || ch >= lineText.Length || !IsIdentCont(lineText[ch]))
            return null;

        var start = ch;
        while (start > 0 && IsIdentCont(lineText[start - 1]))
            start--;
        var end = ch;
        while (end < lineText.Length && IsIdentCont(lineText[end]))
            end++;

        if (!IsIdentStart(lineText[start]))
            return null;

        range = new Range(new Position(position.Line, start), new Position(position.Line, end));
        return lineText.Substring(start, end - start);
    }

    private static string LineText(string text, int lspLine)
    {
        // A malformed client position can carry a negative line; clamp to empty rather than letting the
        // walk fall through and return line 0.
        if (lspLine < 0)
            return string.Empty;

        var lineStart = 0;
        var currentLine = 0;
        for (var i = 0; i < text.Length && currentLine < lspLine; i++)
        {
            if (text[i] == '\n')
            {
                currentLine++;
                lineStart = i + 1;
            }
        }
        if (currentLine < lspLine)
            return string.Empty;

        var lineEnd = text.IndexOf('\n', lineStart);
        if (lineEnd < 0)
            lineEnd = text.Length;
        // Trim a trailing CR so a CRLF document's columns match the engine's char counting.
        if (lineEnd > lineStart && text[lineEnd - 1] == '\r')
            lineEnd--;
        return text.Substring(lineStart, lineEnd - lineStart);
    }

    // Mirrors the Lexer's private IsIdentStart/IsIdentCont (Lexer.cs:348-349) so hyphenated verbs like
    // SIGN-IN and SELECT-ROWS are treated as a single word for hover and range derivation.
    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
    private static bool IsIdentCont(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-';
}
