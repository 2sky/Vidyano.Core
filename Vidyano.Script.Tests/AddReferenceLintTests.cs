using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Parsing;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Parser/grammar coverage for the <c>ADD-REFERENCE</c> verb that confirms an open Add-Reference picker.
/// These assert the parse shape only — the picker frame is opened by an <c>ACTION</c> whose live server
/// result is a <c>Vidyano.AddReference</c> wrapper PO, and confirming posts a real <c>Query.AddReference</c>,
/// so the round-trip needs a live server (a real <see cref="VidyanoSession"/> drives a real Client, there is
/// no mock) and is exercised in the integration tests. Lint-only here, mirroring <see cref="RetryActionLintTests"/>.
/// </summary>
public sealed class AddReferenceLintTests
{
    private static void AssertClean(string body)
    {
        var diags = VidyanoScript.Lint(body);
        Assert.True(diags.Count == 0,
            $"Expected no diagnostics, got: {string.Join("; ", diags.Select(d => $"{d.Kind}: {d.Message}"))}");
    }

    private static ScriptAst ParseClean(string body)
    {
        var lexer = new Lexer(body, "<test>");
        var parser = new Parser(lexer.Tokenize(), lexer.Diagnostics);
        var ast = parser.Parse();
        Assert.True(parser.Diagnostics.Count == 0,
            $"Parse errors: {string.Join("; ", parser.Diagnostics.Select(d => d.Message))}");
        return ast;
    }

    private static T SingleStatement<T>(string body) where T : Statement
    {
        var stmts = ParseClean(body).Steps.SelectMany(s => s.Statements).ToList();
        Assert.Single(stmts);
        return Assert.IsType<T>(stmts[0]);
    }

    // --- parse shapes -------------------------------------------------------------------------

    [Fact]
    public void Bare_ParsesWithNoSelector()
    {
        var stmt = SingleStatement<AddReferenceStmt>("ADD-REFERENCE");
        Assert.Null(stmt.Index);
        Assert.Null(stmt.MatchColumn);
        Assert.Null(stmt.MatchValue);
    }

    [Fact]
    public void PositionalIndex_PopulatesIndexOnly()
    {
        var stmt = SingleStatement<AddReferenceStmt>("ADD-REFERENCE 0");
        Assert.Null(stmt.MatchColumn);
        var lit = Assert.IsType<LiteralExpr>(stmt.Index);
        Assert.Equal(0L, lit.Value);
    }

    [Fact]
    public void Where_PopulatesMatchColumnAndValue()
    {
        var stmt = SingleStatement<AddReferenceStmt>("ADD-REFERENCE WHERE Name = \"Card-007\"");
        Assert.Null(stmt.Index);
        Assert.Equal("Name", stmt.MatchColumn);
        Assert.Equal(ExpectOp.Eq, stmt.MatchOp);
        var lit = Assert.IsType<LiteralExpr>(stmt.MatchValue);
        Assert.Equal("Card-007", lit.Value);
    }

    [Fact]
    public void Where_Interpolated_KeepsExpression()
    {
        var stmt = SingleStatement<AddReferenceStmt>("ADD-REFERENCE WHERE Name = {{@card}}");
        Assert.Equal("Name", stmt.MatchColumn);
        Assert.IsType<InterpExpr>(stmt.MatchValue);
    }

    // --- rejected forms -----------------------------------------------------------------------

    [Fact]
    public void DetailClause_IsRejected()
    {
        // The picker frame is the only target — a Detail clause makes no sense and must be flagged, not
        // silently swallowed.
        var diags = VidyanoScript.Lint("ADD-REFERENCE Detail \"ChargeCards\" WHERE Name = \"x\"");
        Assert.NotEmpty(diags);
        Assert.Contains(diags, d => d.Message.Contains("Detail"));
    }

    [Fact]
    public void Where_MissingValue_Diagnoses()
    {
        var diags = VidyanoScript.Lint("ADD-REFERENCE WHERE Name =");
        Assert.NotEmpty(diags);
    }

    // --- known-verb + smoke -------------------------------------------------------------------

    [Fact]
    public void IsKnownVerb_NotUnknown()
    {
        var diags = VidyanoScript.Lint("ADD-REFERENCE");
        Assert.DoesNotContain(diags, d => d.Message.Contains("Unknown verb"));
    }

    [Theory]
    [InlineData("ADD-REFERENCE")]
    [InlineData("ADD-REFERENCE 0")]
    [InlineData("ADD-REFERENCE WHERE Name = \"Card-007\"")]
    public void DocumentedForms_LintClean(string body)
    {
        AssertClean(body);
    }
}
