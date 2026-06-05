using System.Linq;
using Vidyano.Script.LanguageServer;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Drives <see cref="ViscLanguageService.SemanticTokens"/> through its public surface — the ordered span
/// list, with zero OmniSharp handler reference (mirrors <see cref="LanguageServiceTests"/> +
/// <see cref="RecordingDiagnosticSink"/>). Every assertion is in terms of legend-relative indices read off
/// <see cref="ViscLanguageService.Legend"/>, so the tests stay correct if the frozen legend order changes.
/// </summary>
public sealed class SemanticTokensTests
{
    private const string Uri = "file:///t.visc";

    private static readonly int Keyword = ViscLanguageService.Legend.IndexOf("keyword");
    private static readonly int Type = ViscLanguageService.Legend.IndexOf("type");
    private static readonly int String = ViscLanguageService.Legend.IndexOf("string");
    private static readonly int Number = ViscLanguageService.Legend.IndexOf("number");
    private static readonly int Comment = ViscLanguageService.Legend.IndexOf("comment");
    private static readonly int Variable = ViscLanguageService.Legend.IndexOf("variable");
    private static readonly int Macro = ViscLanguageService.Legend.IndexOf("macro");

    private static async Task<IReadOnlyList<SemanticToken>> Tokenize(string text)
    {
        var svc = new ViscLanguageService(new RecordingDiagnosticSink());
        await svc.DidOpenAsync(Uri, text);
        return svc.SemanticTokens(Uri);
    }

    // === Category mapping (the lexer-flattened Identifier split) ===

    [Fact]
    public async Task Verb_TypeWord_And_FreeIdentifier_GetDistinctCategories()
    {
        // OPEN (verb -> keyword), Query (type-word -> type), Customers (free identifier -> variable).
        var spans = await Tokenize("OPEN Query Customers");

        var open = Assert.Single(spans, s => s.StartChar == 0);
        Assert.Equal(Keyword, open.TokenTypeIndex);
        Assert.Equal(4, open.Length);

        var query = Assert.Single(spans, s => s.StartChar == 5);
        Assert.Equal(Type, query.TokenTypeIndex);
        Assert.Equal(5, query.Length);

        var customers = Assert.Single(spans, s => s.StartChar == 11);
        Assert.Equal(Variable, customers.TokenTypeIndex);
        Assert.Equal(9, customers.Length);
    }

    [Fact]
    public async Task SubKeyword_ColorsAsKeyword_DistinctFromTypeWordAndVariable()
    {
        // SELECT-ROWS (verb), WHERE (sub-keyword -> keyword), Name (free identifier -> variable).
        var spans = await Tokenize("SELECT-ROWS WHERE Name = 1");

        var where = Assert.Single(spans, s => s.StartChar == 12);
        Assert.Equal(Keyword, where.TokenTypeIndex);
        Assert.Equal(5, where.Length);

        var name = Assert.Single(spans, s => s.StartChar == 18);
        Assert.Equal(Variable, name.TokenTypeIndex);
    }

    [Fact]
    public async Task HyphenatedVerb_IsOneSpanIncludingTheHyphen()
    {
        var spans = await Tokenize("SIGN-IN admin / admin");

        var verb = Assert.Single(spans, s => s.StartChar == 0);
        Assert.Equal(Keyword, verb.TokenTypeIndex);
        Assert.Equal(7, verb.Length); // "SIGN-IN" — hyphen included
    }

    [Fact]
    public async Task AtHandle_ColorsAsVariable_AndSpanIncludesTheLeadingAt()
    {
        // The lexeme drops the '@'; the span must still cover it (one wider than the lexeme).
        var spans = await Tokenize("USE @admin");

        var handle = Assert.Single(spans, s => s.StartChar == 4);
        Assert.Equal(Variable, handle.TokenTypeIndex);
        Assert.Equal(6, handle.Length); // '@' + "admin"
    }

    [Fact]
    public async Task NumberLiteral_ColorsAsNumber()
    {
        var spans = await Tokenize("EXPECT TotalItems = 42");

        var number = Assert.Single(spans, s => s.TokenTypeIndex == Number);
        Assert.Equal(20, number.StartChar);
        Assert.Equal(2, number.Length);
    }

    [Fact]
    public async Task NegativeDecimal_ColorsAsOneNumberSpan()
    {
        // The lexer reads "-3.5" as a single Number token; its span must cover the sign + fraction.
        var spans = await Tokenize("SET Score = -3.5");

        var number = Assert.Single(spans, s => s.TokenTypeIndex == Number);
        Assert.Equal(12, number.StartChar);
        Assert.Equal(4, number.Length);
    }

    [Fact]
    public async Task BooleanAndNullLiterals_CarryNoSemanticColor()
    {
        // true/false/null arrive as TokenKind.Literal, which v1 leaves uncolored (handled by the
        // TextMate constant rule, not the semantic path).
        var spans = await Tokenize("SET Flag = true");

        Assert.DoesNotContain(spans, s => s.StartChar == 11); // where "true" sits
    }

    // === Strings and {{holes}} ===

    [Fact]
    public async Task PlainString_IsOneStringSpanCoveringTheQuotes()
    {
        var spans = await Tokenize("SET Name = \"Acme\"");

        var str = Assert.Single(spans, s => s.TokenTypeIndex == String);
        Assert.Equal(11, str.StartChar);          // the opening quote
        Assert.Equal(6, str.Length);              // "Acme" + both quotes
    }

    [Fact]
    public async Task StringWithHole_SplitsIntoStringRunsAndMacroSubSpans()
    {
        // "Acme {{@id}}" -> string-run / `{{` / content (@id) / `}}` — never one flat span.
        var spans = await Tokenize("SET Name = \"Acme {{@id}}\"");

        var stringSpans = spans.Where(s => s.TokenTypeIndex == String).ToList();
        var macroSpans = spans.Where(s => s.TokenTypeIndex == Macro).OrderBy(s => s.StartChar).ToList();

        Assert.NotEmpty(stringSpans);
        // Three macro sub-spans: the `{{`, the inner content, the `}}`.
        Assert.Equal(3, macroSpans.Count);

        // `{{` opens after `"Acme ` (col 11 = quote, 17 = first '{').
        Assert.Equal(17, macroSpans[0].StartChar);
        Assert.Equal(2, macroSpans[0].Length);          // `{{`
        Assert.Equal(19, macroSpans[1].StartChar);      // "@id"
        Assert.Equal(3, macroSpans[1].Length);
        Assert.Equal(22, macroSpans[2].StartChar);      // `}}`
        Assert.Equal(2, macroSpans[2].Length);

        // The leading string run starts at the opening quote and stops at the hole opener.
        var leadingRun = stringSpans.Single(s => s.StartChar == 11);
        Assert.Equal(6, leadingRun.Length); // `"Acme ` up to the `{{`
    }

    [Fact]
    public async Task EscapedBraceInString_IsNotTreatedAsHoleOpener()
    {
        // `\{` is an escape, part of the string run — no macro span for it.
        var spans = await Tokenize("SET Name = \"a\\{b\"");

        Assert.DoesNotContain(spans, s => s.TokenTypeIndex == Macro);
        Assert.Contains(spans, s => s.TokenTypeIndex == String);
    }

    [Fact]
    public async Task StandaloneInterpolation_SplitsIntoMacroSubSpans()
    {
        // A bare {{...}} in a value position colors like an in-string hole: `{{` / content / `}}` as macro —
        // the identical construct must not read differently just because it isn't inside a string.
        var spans = await Tokenize("SET Name = {{@uuid}}");

        var macroSpans = spans.Where(s => s.TokenTypeIndex == Macro).OrderBy(s => s.StartChar).ToList();
        Assert.Equal(3, macroSpans.Count);
        Assert.Equal(11, macroSpans[0].StartChar); // `{{`
        Assert.Equal(2, macroSpans[0].Length);
        Assert.Equal(13, macroSpans[1].StartChar); // "@uuid"
        Assert.Equal(5, macroSpans[1].Length);
        Assert.Equal(18, macroSpans[2].StartChar); // `}}`
        Assert.Equal(2, macroSpans[2].Length);
    }

    [Fact]
    public async Task StandaloneInterpolation_WithQuotedFallback_KeepsTheQuoteAsHoleContent()
    {
        // A bare hole may carry a quoted fallback (`{{env:X ?? "y"}}`); unlike an in-string hole its content
        // runs to `}}` (Lexer.ReadInterp), so the `"` is content, never a string opener that truncates it.
        var spans = await Tokenize("SET X = {{env:Y ?? \"z\"}}");

        var macroSpans = spans.Where(s => s.TokenTypeIndex == Macro).OrderBy(s => s.StartChar).ToList();
        Assert.Equal(3, macroSpans.Count);
        Assert.Equal(8, macroSpans[0].StartChar);   // `{{`
        Assert.Equal(10, macroSpans[1].StartChar);  // content begins right after `{{`
        Assert.Equal(12, macroSpans[1].Length);     // `env:Y ?? "z"` — the quotes are content, not stripped
        Assert.Equal(22, macroSpans[2].StartChar);  // `}}`

        Assert.DoesNotContain(spans, s => s.TokenTypeIndex == String); // the fallback quote is not a string
    }

    // === Comments: dropped #...EOL re-derivation + ### step headers ===

    [Fact]
    public async Task TrailingLineComment_IsReDerivedAsCommentSpan()
    {
        // The lexer drops `#...EOL`; the producer must re-scan it and color from the '#' to EOL.
        var spans = await Tokenize("OPEN Query Customers # a note");

        var comment = Assert.Single(spans, s => s.TokenTypeIndex == Comment);
        Assert.Equal(0, comment.Line);
        Assert.Equal(21, comment.StartChar);      // the '#'
        Assert.Equal(8, comment.Length);          // "# a note"
    }

    [Fact]
    public async Task HashInsideString_IsNotAComment()
    {
        // A '#' inside a string literal must not start a comment span.
        var spans = await Tokenize("SET Name = \"a # b\"");

        Assert.DoesNotContain(spans, s => s.TokenTypeIndex == Comment);
    }

    [Fact]
    public async Task HashInsideStandaloneInterpolation_IsNotAComment()
    {
        // A '#' inside a bare {{...}} hole is hole content (Lexer.ReadInterp), not a comment-to-EOL — the
        // dropped-comment re-scan must track hole state, not just string state.
        var spans = await Tokenize("SET X = {{a # b}}");

        Assert.DoesNotContain(spans, s => s.TokenTypeIndex == Comment);
    }

    [Fact]
    public async Task BackslashBeforeNewlineInString_ContinuesString_SoFollowingHashIsNotAComment()
    {
        // The lexer's escape consumes the char after `\` unconditionally, so `\`+newline continues the string
        // onto the next line (a valid line continuation). A '#' on that continued line is string content; the
        // re-scan must not mistake it for a comment-to-EOL (the bug was resetting string state at the newline).
        var spans = await Tokenize("SET X = \"a\\\n# b\"");

        Assert.DoesNotContain(spans, s => s.TokenTypeIndex == Comment);
    }

    [Fact]
    public async Task BackslashBeforeCrlfInString_ContinuesString_SoFollowingHashIsNotAComment()
    {
        // CRLF parity with the LF case above: on a Windows-authored (CRLF) file `\`+`\r\n` is the same string
        // continuation, so the next line's '#' is string content, not a comment. The earlier code consumed the
        // CR, reset string state at the bare LF, and mis-colored the '#' — the dropped-comment re-scan must keep
        // string state across the whole CRLF, matching the lexer.
        var spans = await Tokenize("SET X = \"a\\\r\n# b\"");

        Assert.DoesNotContain(spans, s => s.TokenTypeIndex == Comment);
    }

    [Fact]
    public async Task StepHeader_ColorsAsCommentToEol()
    {
        var spans = await Tokenize("### Section one\nOPEN Query Customers");

        var header = Assert.Single(spans, s => s.Line == 0 && s.TokenTypeIndex == Comment);
        Assert.Equal(0, header.StartChar);
        Assert.Equal(15, header.Length); // "### Section one"
    }

    [Fact]
    public async Task StepHeaderLine_IsNotAlsoColoredAsAHashComment()
    {
        // The '###' line must produce exactly one comment span (the StepHeader), not a second one from the
        // dropped-comment re-scan double-counting the leading '#'.
        var spans = await Tokenize("### Header");

        var comments = spans.Where(s => s.TokenTypeIndex == Comment).ToList();
        Assert.Single(comments);
    }

    // === Ordering / overlap invariant ===

    [Fact]
    public async Task Spans_AreStrictlyAscending_AndNonOverlapping()
    {
        var text = "### Step\nOPEN Query Customers # note\nSET Name = \"Acme {{@id}}\" # x";
        var spans = await Tokenize(text);

        Assert.NotEmpty(spans);
        for (var i = 1; i < spans.Count; i++)
        {
            var prev = spans[i - 1];
            var cur = spans[i];
            var ascending = cur.Line > prev.Line || (cur.Line == prev.Line && cur.StartChar >= prev.StartChar);
            Assert.True(ascending, $"span {i} ({cur.Line},{cur.StartChar}) precedes ({prev.Line},{prev.StartChar})");
            if (cur.Line == prev.Line)
                Assert.True(cur.StartChar >= prev.StartChar + prev.Length,
                    $"span {i} overlaps the previous on line {cur.Line}");
        }
    }

    // === Coordinate correctness: astral widening + CRLF trimming ===

    [Fact]
    public async Task AstralCharacterInString_WidensItsOwnSpanToUtf16Units()
    {
        // The emoji is one Unicode char but two UTF-16 code units. The string span length must be measured
        // in UTF-16 units (length re-derived through ToLsp, not a fresh surrogate walk).
        var spans = await Tokenize("SET Name = \"\U0001F600\"\nEXPECT TotalItems = 1");

        // The emoji string occupies cols 11..14 in UTF-16 units: quote + 2 surrogate units + quote = 4.
        var str = Assert.Single(spans, s => s.Line == 0 && s.TokenTypeIndex == String);
        Assert.Equal(11, str.StartChar);
        Assert.Equal(4, str.Length);

        // The next line's verb is unaffected — it begins at col 0.
        var expect = Assert.Single(spans, s => s.Line == 1 && s.StartChar == 0);
        Assert.Equal(Keyword, expect.TokenTypeIndex);
    }

    [Fact]
    public async Task Crlf_DoesNotShiftColumnsOnFollowingLine()
    {
        // A CRLF document must produce the same columns as an LF one — the CR is trimmed by LineText.
        var spansLf = await Tokenize("OPEN Query X\nEXPECT TotalItems = 1");
        var spansCrlf = await Tokenize("OPEN Query X\r\nEXPECT TotalItems = 1");

        var expectLf = Assert.Single(spansLf, s => s.Line == 1 && s.TokenTypeIndex == Keyword);
        var expectCrlf = Assert.Single(spansCrlf, s => s.Line == 1 && s.TokenTypeIndex == Keyword);
        Assert.Equal(expectLf.StartChar, expectCrlf.StartChar);
        Assert.Equal(expectLf.Length, expectCrlf.Length);
    }

    // === Degenerate inputs ===

    [Fact]
    public void UnknownUri_ReturnsEmptyList_NoThrow()
    {
        var svc = new ViscLanguageService(new RecordingDiagnosticSink());
        var spans = svc.SemanticTokens("file:///never-opened.visc");
        Assert.Empty(spans);
    }

    [Fact]
    public async Task ClosedDocument_ReturnsEmptyList()
    {
        var svc = new ViscLanguageService(new RecordingDiagnosticSink());
        await svc.DidOpenAsync(Uri, "OPEN Query Customers");
        Assert.NotEmpty(svc.SemanticTokens(Uri));

        await svc.DidCloseAsync(Uri);
        Assert.Empty(svc.SemanticTokens(Uri));
    }

    [Fact]
    public async Task EmptyDocument_ReturnsEmptyList()
    {
        var svc = new ViscLanguageService(new RecordingDiagnosticSink());
        await svc.DidOpenAsync(Uri, "");
        Assert.Empty(svc.SemanticTokens(Uri));
    }
}
