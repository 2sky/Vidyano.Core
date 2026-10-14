using System.Collections.Generic;
using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Parsing;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Parser/grammar coverage for <c>INSTANT-SEARCH</c>, the <c>EXPECT InstantSearch.*</c> subjects and the
/// <c>OPEN-ROW InstantSearch …</c> forms. The live round-trip (the server's <c>Instant</c> endpoint) is covered by
/// <c>Vidyano.Script.IntegrationTests.InstantSearchTests</c>; these are server-free parse + lint checks.
/// </summary>
public sealed class InstantSearchLintTests
{
    private static T SingleStatement<T>(string body) where T : Statement
    {
        var lexer = new Lexer(body, "<test>");
        var parser = new Parser(lexer.Tokenize(), lexer.Diagnostics);
        var ast = parser.Parse();
        Assert.True(parser.Diagnostics.Count == 0,
            $"Parse errors: {string.Join("; ", parser.Diagnostics.Select(d => d.Message))}");
        var stmts = ast.Steps.SelectMany(s => s.Statements).ToList();
        Assert.Single(stmts);
        return Assert.IsType<T>(stmts[0]);
    }

    private static IReadOnlyList<Diagnostic> Lint(string body) => VidyanoScript.Lint(body);

    [Fact]
    public void InstantSearch_ParsesLiteralText()
    {
        var stmt = SingleStatement<InstantSearchStmt>("INSTANT-SEARCH \"acme\"");
        var lit = Assert.IsType<LiteralExpr>(stmt.Text);
        Assert.Equal("acme", lit.Value);
    }

    [Fact]
    public void InstantSearch_ParsesInterpolatedText()
    {
        var stmt = SingleStatement<InstantSearchStmt>("INSTANT-SEARCH \"{{name}}\"");
        Assert.IsType<StringInterpExpr>(stmt.Text);
    }

    [Fact]
    public void InstantSearch_WithNoText_IsRejected() => Assert.NotEmpty(Lint("INSTANT-SEARCH"));

    [Fact]
    public void InstantSearch_IsARecognizedVerb()
    {
        var diags = Lint("INSTANT-SEARCH \"acme\"");
        Assert.DoesNotContain(diags, d => d.Message.StartsWith("Unknown verb", System.StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("EXPECT InstantSearch.Enabled = true", ExpectSubjectKind.InstantSearchEnabled)]
    [InlineData("EXPECT InstantSearch.Count = 2", ExpectSubjectKind.InstantSearchCount)]
    [InlineData("EXPECT InstantSearch.Count IS NULL", ExpectSubjectKind.InstantSearchCount)]
    public void Expect_ParsesGateAndCount(string body, ExpectSubjectKind kind) =>
        Assert.Equal(kind, SingleStatement<ExpectStmt>(body).Subject.Kind);

    [Theory]
    [InlineData("Label")]
    [InlineData("ObjectId")]
    [InlineData("Breadcrumb")]
    [InlineData("Id")]
    public void Expect_ParsesRowField(string field)
    {
        var e = SingleStatement<ExpectStmt>($"EXPECT InstantSearch[1].{field} = \"x\"");
        Assert.Equal(ExpectSubjectKind.InstantSearchRow, e.Subject.Kind);
        Assert.Equal(field, e.Subject.MetadataKey);
        Assert.Equal(1L, Assert.IsType<LiteralExpr>(e.Subject.RowIndex).Value);
    }

    [Fact]
    public void Requires_AcceptsInstantSearchSubject() =>
        Assert.Empty(Lint("REQUIRES InstantSearch.Enabled = true"));

    [Theory]
    [InlineData("EXPECT InstantSearch = 1")]
    [InlineData("EXPECT InstantSearch.Total = 1")]
    [InlineData("EXPECT InstantSearch[0].Name = \"x\"")]
    [InlineData("EXPECT InstantSearch[0] = \"x\"")]
    public void Expect_MalformedSubject_IsRejected(string body) => Assert.NotEmpty(Lint(body));

    [Fact]
    public void OpenRow_ParsesIndex()
    {
        var stmt = SingleStatement<OpenRowStmt>("OPEN-ROW InstantSearch 0");
        Assert.True(stmt.InstantSearch);
        Assert.Equal(0L, Assert.IsType<LiteralExpr>(stmt.Index).Value);
        Assert.Null(stmt.MatchColumn);
    }

    [Fact]
    public void OpenRow_ParsesWhereAndExpectingError()
    {
        var stmt = SingleStatement<OpenRowStmt>("OPEN-ROW InstantSearch WHERE ObjectId = \"42\" AS @p EXPECTING ERROR");
        Assert.True(stmt.InstantSearch);
        Assert.True(stmt.ExpectError);
        Assert.Equal("ObjectId", stmt.MatchColumn);
        Assert.Equal("p", stmt.AsHandle);
    }

    [Fact]
    public void OpenRow_WhereUnknownField_IsRejected() =>
        Assert.NotEmpty(Lint("OPEN-ROW InstantSearch WHERE Name = \"x\""));

    [Fact]
    public void OpenRow_WithNoTarget_IsRejected() =>
        Assert.NotEmpty(Lint("OPEN-ROW InstantSearch"));

    [Theory]
    [InlineData("OPEN-ROW 0")]
    [InlineData("OPEN-ROW WHERE Name = \"InstantSearch\"")]
    [InlineData("OPEN-ROW WHERE InstantSearch = \"x\"")]
    [InlineData("OPEN-ROW Detail \"Lines\" 1")]
    public void OpenRow_ExistingForms_AreUnaffected(string body) =>
        Assert.False(SingleStatement<OpenRowStmt>(body).InstantSearch);
}
