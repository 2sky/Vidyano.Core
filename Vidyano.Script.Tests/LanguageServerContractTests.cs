using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.LanguageServer;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Supplementary contract coverage for the static language server: gaps in the coordinate math, the
/// diagnostic round-trip's wire shape (Source/Code/Message), CRLF handling, the hover Range, and catalog
/// integrity. Complements <see cref="CoordinateMappingTests"/>, <see cref="LanguageServiceTests"/>, and
/// <see cref="VerbCatalogReconciliationTests"/> without restating them.
/// </summary>
public sealed class LanguageServerContractTests
{
    private const string Uri = "file:///t.visc";

    // === Coordinate math edges (statics, no service instance) ===

    [Fact]
    public void ToLsp_AstralCharacterOnSecondLine_ShiftsColumnOnThatLineOnly()
    {
        // The astral char lives on line 2; ToLsp must walk line 2's text (not line 1) to widen the column.
        var text = "SET A = 1\n\U0001F600X";
        var loc = new SourceLocation("<t>", 2, 2); // line 2, column 2 = the 'X' (1-based char)
        var (line, ch) = ViscLanguageService.ToLsp(loc, text);
        Assert.Equal(1, line);
        Assert.Equal(2, ch); // the surrogate pair pushed 'X' to code unit 2 on its own line
    }

    [Fact]
    public void ToLsp_ColumnPastEndOfLine_ClampsToLineLengthInCodeUnits()
    {
        // A column beyond the line's characters must not walk off the end; it clamps at the line length.
        var text = "SET"; // 3 chars
        var loc = new SourceLocation("<t>", 1, 99);
        var (line, ch) = ViscLanguageService.ToLsp(loc, text);
        Assert.Equal(0, line);
        Assert.Equal(3, ch); // clamped to the 3 code units of "SET"
    }

    [Fact]
    public void RangeAt_IndentedHyphenatedVerb_SpansTheWholeVerbAtItsColumn()
    {
        // Leading spaces shift the verb right; the range must start at the verb, not column 0, and the
        // hyphen must stay inside the single word.
        var text = "    SIGN-IN admin / pw";
        var loc = new SourceLocation("<t>", 1, 8); // inside "SIGN-IN" (1-based char; col 5 = 'S')
        var range = ViscLanguageService.RangeAt(loc, text);
        Assert.Equal(4, range.Start.Character);  // "SIGN-IN" starts after 4 spaces
        Assert.Equal(11, range.End.Character);   // 4 + len("SIGN-IN")=7 => 11
    }

    [Fact]
    public void ToLsp_CrlfDocument_CountsColumnsWithoutTheTrailingCarriageReturn()
    {
        // The engine counts characters per line; LineText trims a trailing CR so a CRLF doc's columns line
        // up with the engine's char counting rather than being thrown off by the \r.
        var text = "SET A = 1\r\nEXPECT B = 2";
        var loc = new SourceLocation("<t>", 2, 1); // line 2, column 1 = 'E' of EXPECT
        var (line, ch) = ViscLanguageService.ToLsp(loc, text);
        Assert.Equal(1, line);
        Assert.Equal(0, ch);
    }

    // === Diagnostic round-trip wire shape (through the public service + recording sink) ===

    [Fact]
    public async Task DidOpen_Diagnostic_CarriesViscSourceAndKindAsCode()
    {
        var sink = new RecordingDiagnosticSink();
        var svc = new ViscLanguageService(sink);

        await svc.DidOpenAsync(Uri, "EXPEKT TotalItems = 3");

        var d = Assert.Single(sink.Last(Uri));
        Assert.Equal("visc", d.Source);
        Assert.Equal("parse-unknown-verb", d.Code!.Value.String);
    }

    [Fact]
    public async Task DidOpen_Diagnostic_FoldsTheHintIntoTheMessage()
    {
        var sink = new RecordingDiagnosticSink();
        var svc = new ViscLanguageService(sink);

        await svc.DidOpenAsync(Uri, "EXPEKT TotalItems = 3");

        // The engine attaches a "did you mean" hint to an unknown verb; the mapper appends it to Message
        // so a flat LSP message string carries both halves.
        var d = Assert.Single(sink.Last(Uri));
        Assert.StartsWith("Unknown verb 'EXPEKT'.", d.Message);
        Assert.True(d.Message.Length > "Unknown verb 'EXPEKT'.".Length, "the hint should be folded in");
    }

    [Fact]
    public async Task DidOpen_IndentedUnknownVerb_RangeStartsAtTheVerbNotColumnZero()
    {
        var sink = new RecordingDiagnosticSink();
        var svc = new ViscLanguageService(sink);

        await svc.DidOpenAsync(Uri, "   EXPEKT TotalItems = 3"); // 3 leading spaces

        var d = Assert.Single(sink.Last(Uri));
        Assert.Equal(0, d.Range.Start.Line);
        Assert.Equal(3, d.Range.Start.Character);
        Assert.Equal(9, d.Range.End.Character); // 3 + len("EXPEKT")=6 => 9
    }

    [Fact]
    public async Task DidChange_ReplacesDocumentText_SoStaleHoverIsGone()
    {
        var sink = new RecordingDiagnosticSink();
        var svc = new ViscLanguageService(sink);

        await svc.DidOpenAsync(Uri, "OPEN MenuItem X");
        Assert.NotNull(svc.Hover(Uri, new Position(0, 1))); // "OPEN" resolves

        await svc.DidChangeAsync(Uri, "EXPEKT TotalItems = 3"); // full-document replace

        // The old verb no longer exists at that position; the change replaced the cached text.
        Assert.Null(svc.Hover(Uri, new Position(0, 1)));
    }

    // === Hover range (the implementer asserts content; pin the Range too) ===

    [Fact]
    public async Task Hover_OnHyphenatedVerb_RangeSpansTheWholeVerb()
    {
        var sink = new RecordingDiagnosticSink();
        var svc = new ViscLanguageService(sink);
        await svc.DidOpenAsync(Uri, "SIGN-IN admin / admin");

        var hover = svc.Hover(Uri, new Position(0, 6)); // inside "IN", past the hyphen

        Assert.NotNull(hover);
        Assert.NotNull(hover!.Range);
        Assert.Equal(0, hover.Range!.Start.Character);
        Assert.Equal(7, hover.Range.End.Character); // len("SIGN-IN") = 7
    }

    [Fact]
    public async Task Hover_AtEndOfLine_ReturnsNull()
    {
        var sink = new RecordingDiagnosticSink();
        var svc = new ViscLanguageService(sink);
        await svc.DidOpenAsync(Uri, "OPEN");

        // Position one past the last character of "OPEN" is the EOL slot — no word there.
        Assert.Null(svc.Hover(Uri, new Position(0, 4)));
    }

    // === Catalog integrity beyond "has an example" ===

    [Fact]
    public void EveryVerb_HasNonEmptySyntaxAndSummary()
    {
        Assert.All(VerbCatalog.All, v =>
        {
            Assert.False(string.IsNullOrWhiteSpace(v.Name), "verb name must be set");
            Assert.False(string.IsNullOrWhiteSpace(v.Syntax), $"{v.Name} must have a syntax form");
            Assert.False(string.IsNullOrWhiteSpace(v.Summary), $"{v.Name} must have a summary");
            Assert.False(string.IsNullOrWhiteSpace(v.Category), $"{v.Name} must have a category");
        });
    }

    [Fact]
    public async Task EveryVerb_RendersAHoverWithoutThrowing()
    {
        // Hover formatting reads Syntax/Summary/MarkdownDoc/Examples for each verb; exercise all of them so
        // a malformed catalog entry surfaces here rather than only when a user hovers that verb.
        var sink = new RecordingDiagnosticSink();
        var svc = new ViscLanguageService(sink);
        foreach (var v in VerbCatalog.All)
        {
            var uri = $"file:///{v.Name}.visc";
            await svc.DidOpenAsync(uri, v.Name);
            var hover = svc.Hover(uri, new Position(0, 0));
            Assert.NotNull(hover);
            Assert.Contains($"**{v.Name}**", hover!.Contents.MarkupContent!.Value);
        }
    }
}
