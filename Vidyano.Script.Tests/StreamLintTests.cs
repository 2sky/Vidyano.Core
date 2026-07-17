using System.Collections.Generic;
using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Parsing;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Parser/grammar coverage for the <c>EXPECT Stream</c> / <c>Stream.Name</c> / <c>Stream.Length</c> /
/// <c>Stream.Text</c> subjects. The live auto-fetch round-trip is covered by
/// <c>Vidyano.Script.IntegrationTests.StreamTests</c>; these are server-free parse + lint checks.
/// </summary>
public sealed class StreamLintTests
{
    private static ExpectStmt SingleExpect(string body)
    {
        var lexer = new Lexer(body, "<test>");
        var parser = new Parser(lexer.Tokenize(), lexer.Diagnostics);
        var ast = parser.Parse();
        Assert.True(parser.Diagnostics.Count == 0,
            $"Parse errors: {string.Join("; ", parser.Diagnostics.Select(d => d.Message))}");
        var stmts = ast.Steps.SelectMany(s => s.Statements).ToList();
        Assert.Single(stmts);
        return Assert.IsType<ExpectStmt>(stmts[0]);
    }

    private static IReadOnlyList<Diagnostic> Lint(string body) => VidyanoScript.Lint(body);

    [Fact]
    public void ExpectStream_ParsesAsStreamSubject()
    {
        var stmt = SingleExpect("EXPECT Stream IS NULL");
        Assert.Equal(ExpectSubjectKind.Stream, stmt.Subject.Kind);
        Assert.Equal(ExpectOp.IsNull, stmt.Op);
    }

    [Fact]
    public void ExpectStreamName_ParsesAsStreamNameSubject()
    {
        var stmt = SingleExpect("EXPECT Stream.Name = \"invoice.pdf\"");
        Assert.Equal(ExpectSubjectKind.StreamName, stmt.Subject.Kind);
    }

    [Fact]
    public void ExpectStreamLength_ParsesAsStreamLengthSubject()
    {
        var stmt = SingleExpect("EXPECT Stream.Length > 0");
        Assert.Equal(ExpectSubjectKind.StreamLength, stmt.Subject.Kind);
        Assert.Equal(ExpectOp.Gt, stmt.Op);
    }

    [Fact]
    public void ExpectStreamText_ParsesAsStreamTextSubject()
    {
        var stmt = SingleExpect("EXPECT Stream.Text CONTAINS \"%PDF\"");
        Assert.Equal(ExpectSubjectKind.StreamText, stmt.Subject.Kind);
        Assert.Equal(ExpectOp.Contains, stmt.Op);
    }

    [Fact]
    public void ExpectStream_UnknownProperty_IsRejected()
    {
        var diags = Lint("EXPECT Stream.Bogus = \"x\"");
        Assert.NotEmpty(diags);
        Assert.Contains(diags, d => d.Message.Contains("Stream has no property"));
    }
}
