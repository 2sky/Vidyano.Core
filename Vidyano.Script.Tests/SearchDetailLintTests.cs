using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Parsing;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Parser/grammar coverage for the <c>SEARCH Detail "&lt;name&gt;"</c> form — the side-effect-free way to
/// load a named detail query in place so a following <c>EXPECT Detail … TotalItems</c> sees server
/// state. Lint-only: the actual search needs a live session, like the rest of the session-driving
/// behavior. Mirrors <see cref="ExpectingErrorLintTests"/>.
/// </summary>
public sealed class SearchDetailLintTests
{
    private static void AssertClean(string body)
    {
        var diags = VidyanoScript.Lint(body);
        Assert.True(diags.Count == 0,
            $"Expected no diagnostics, got: {string.Join("; ", diags.Select(d => $"{d.Kind}: {d.Message}"))}");
    }

    private static SearchStmt SingleSearch(string body)
    {
        var lexer = new Lexer(body, "<test>");
        var parser = new Parser(lexer.Tokenize(), lexer.Diagnostics);
        var ast = parser.Parse();
        Assert.True(parser.Diagnostics.Count == 0,
            $"Parse errors: {string.Join("; ", parser.Diagnostics.Select(d => d.Message))}");
        var stmts = ast.Steps.SelectMany(s => s.Statements).ToList();
        Assert.Single(stmts);
        return Assert.IsType<SearchStmt>(stmts[0]);
    }

    [Fact]
    public void Search_CurrentQuery_NoDetail()
    {
        var stmt = SingleSearch("SEARCH \"acme\"");
        Assert.Null(stmt.DetailName);
        Assert.NotNull(stmt.Text);
    }

    [Fact]
    public void Search_Detail_QuotedName_NoText()
    {
        var stmt = SingleSearch("SEARCH Detail \"OrderLines\"");
        Assert.Equal("OrderLines", stmt.DetailName);
        Assert.Null(stmt.Text);
    }

    [Fact]
    public void Search_Detail_BareName_NoText()
    {
        var stmt = SingleSearch("SEARCH Detail OrderLines");
        Assert.Equal("OrderLines", stmt.DetailName);
        Assert.Null(stmt.Text);
    }

    [Fact]
    public void Search_Detail_WithText()
    {
        var stmt = SingleSearch("SEARCH Detail \"OrderLines\" \"open\"");
        Assert.Equal("OrderLines", stmt.DetailName);
        var lit = Assert.IsType<LiteralExpr>(stmt.Text);
        Assert.Equal("open", lit.Value);
    }

    [Fact]
    public void Search_QuotedDetailWord_IsLiteralText_NotDetailTarget()
    {
        // A bare identifier `Detail` is the keyword; quoting it searches the current query for the
        // literal word "Detail" instead of retargeting a detail query.
        var stmt = SingleSearch("SEARCH \"Detail\"");
        Assert.Null(stmt.DetailName);
        var lit = Assert.IsType<LiteralExpr>(stmt.Text);
        Assert.Equal("Detail", lit.Value);
    }

    [Fact]
    public void Search_Detail_MissingName_Diagnoses()
    {
        var diags = VidyanoScript.Lint("SEARCH Detail");
        Assert.NotEmpty(diags);
    }

    [Theory]
    [InlineData("SEARCH \"acme\"")]
    [InlineData("SEARCH Detail \"OrderLines\"")]
    [InlineData("SEARCH Detail OrderLines")]
    [InlineData("SEARCH Detail \"OrderLines\" \"open\"")]
    public void DocumentedForms_LintClean(string body)
    {
        AssertClean(body);
    }
}
