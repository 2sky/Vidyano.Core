using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Vidyano.Script.LanguageServer;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Drives <see cref="ViscLanguageService"/> through its public surface and the one
/// <see cref="IDiagnosticSink"/> port — no stdio, no JSON-RPC, no real LSP client. Asserts the
/// document→publish round-trip and hover, exactly as an editor would observe them.
/// </summary>
public sealed class LanguageServiceTests
{
    private const string Uri = "file:///t.visc";

    [Fact]
    public async Task DidOpen_UnknownVerb_PublishesError()
    {
        var sink = new RecordingDiagnosticSink();
        var svc = new ViscLanguageService(sink);

        await svc.DidOpenAsync(Uri, "SIGN-IN steve / pw\nEXPEKT TotalItems = 3");

        var published = Assert.Single(sink.Last(Uri));
        Assert.Equal("parse-unknown-verb", published.Code!.Value.String);
        Assert.Equal(DiagnosticSeverity.Error, published.Severity);
        Assert.Equal(1, published.Range.Start.Line); // 0-based -> source line 2
        Assert.Equal(0, published.Range.Start.Character);
        Assert.Equal(6, published.Range.End.Character); // "EXPEKT" is 6 chars wide
    }

    [Fact]
    public async Task DidOpen_UnterminatedString_PublishesLexErrorAsError()
    {
        var sink = new RecordingDiagnosticSink();
        var svc = new ViscLanguageService(sink);

        // The lexer flags an unterminated string when a newline interrupts the literal.
        await svc.DidOpenAsync(Uri, "SET Name = \"unterminated\nEXPECT TotalItems = 1");

        Assert.Contains(sink.Last(Uri), d =>
            d.Code!.Value.String == "parse-unterminated-string" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task DidChange_CleanDocument_PublishesEmptyToClearStaleDiagnostics()
    {
        var sink = new RecordingDiagnosticSink();
        var svc = new ViscLanguageService(sink);

        await svc.DidOpenAsync(Uri, "EXPEKT TotalItems = 3"); // one error
        Assert.NotEmpty(sink.Last(Uri));

        await svc.DidChangeAsync(Uri, "EXPECT TotalItems = 3"); // now clean

        Assert.Empty(sink.Last(Uri)); // empty publish clears the prior set
    }

    [Fact]
    public async Task DidClose_ClearsDiagnostics()
    {
        var sink = new RecordingDiagnosticSink();
        var svc = new ViscLanguageService(sink);

        await svc.DidOpenAsync(Uri, "EXPEKT TotalItems = 3");
        await svc.DidCloseAsync(Uri);

        Assert.Empty(sink.Last(Uri));
    }

    [Fact]
    public async Task Hover_InsideVerb_ReturnsVerbMarkdown()
    {
        var sink = new RecordingDiagnosticSink();
        var svc = new ViscLanguageService(sink);
        await svc.DidOpenAsync(Uri, "OPEN MenuItem Sales/Customers");

        var hover = svc.Hover(Uri, new Position(0, 2)); // inside "OPEN"

        Assert.NotNull(hover);
        Assert.True(hover!.Contents.HasMarkupContent);
        Assert.Contains("navigation stack", hover.Contents.MarkupContent!.Value);
    }

    [Fact]
    public async Task Hover_OnHyphenatedVerb_TreatsWholeVerbAsOneWord()
    {
        var sink = new RecordingDiagnosticSink();
        var svc = new ViscLanguageService(sink);
        await svc.DidOpenAsync(Uri, "SIGN-IN admin / admin");

        // Position past the hyphen, inside "IN" — the whole "SIGN-IN" must resolve, not "SIGN".
        var hover = svc.Hover(Uri, new Position(0, 6));

        Assert.NotNull(hover);
        Assert.Contains("**SIGN-IN**", hover!.Contents.MarkupContent!.Value);
    }

    [Fact]
    public async Task Hover_OnWhitespace_ReturnsNull()
    {
        var sink = new RecordingDiagnosticSink();
        var svc = new ViscLanguageService(sink);
        await svc.DidOpenAsync(Uri, "OPEN MenuItem X");

        Assert.Null(svc.Hover(Uri, new Position(0, 4))); // the space after OPEN
    }

    [Fact]
    public async Task Hover_OnUnknownLexeme_ReturnsNull()
    {
        var sink = new RecordingDiagnosticSink();
        var svc = new ViscLanguageService(sink);
        await svc.DidOpenAsync(Uri, "EXPEKT TotalItems = 3");

        Assert.Null(svc.Hover(Uri, new Position(0, 2))); // inside the misspelled verb
    }

    [Fact]
    public void Hover_UnknownDocument_ReturnsNull()
    {
        var svc = new ViscLanguageService(new RecordingDiagnosticSink());
        Assert.Null(svc.Hover("file:///never-opened.visc", new Position(0, 0)));
    }
}
