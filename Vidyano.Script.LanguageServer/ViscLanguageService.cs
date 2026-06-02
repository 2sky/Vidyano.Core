using System.Collections.Concurrent;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Vidyano.Script;
using Vidyano.Script.Diagnostics;
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
