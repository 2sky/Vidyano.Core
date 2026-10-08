using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Parsing;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Parser/grammar coverage for the <c>EXPECTING ERROR</c> suffix on the fallible verbs SAVE,
/// ACTION and SEARCH. The suffix flips the verb's success polarity at run time (it passes iff the server returns
/// an error notification); these tests only assert the parse shape — runtime polarity needs a live
/// server, like the rest of the session-driving behavior. Lint-only, mirroring
/// <see cref="GrammarRefreshLintTests"/>.
/// </summary>
public sealed class ExpectingErrorLintTests
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

    // --- SAVE EXPECTING ERROR ------------------------------------------------------------------

    [Fact]
    public void Save_ExpectingError_SetsFlag()
    {
        var stmt = SingleStatement<SaveStmt>("SAVE EXPECTING ERROR");
        Assert.True(stmt.ExpectError);
        Assert.Null(stmt.Scope);
    }

    [Fact]
    public void Save_Bare_DoesNotSetFlag()
    {
        var stmt = SingleStatement<SaveStmt>("SAVE");
        Assert.False(stmt.ExpectError);
    }

    [Fact]
    public void SaveInitial_ExpectingError_KeepsScopeAndSetsFlag()
    {
        var stmt = SingleStatement<SaveStmt>("SAVE @initial EXPECTING ERROR");
        Assert.Equal("initial", stmt.Scope);
        Assert.True(stmt.ExpectError);
    }

    // --- ACTION EXPECTING ERROR (every action form) --------------------------------------------

    [Fact]
    public void Action_Bare_ExpectingError_SetsFlag()
    {
        var stmt = SingleStatement<ActionStmt>("ACTION Delete EXPECTING ERROR");
        Assert.Equal("Delete", stmt.ActionName);
        Assert.True(stmt.ExpectError);
        Assert.Null(stmt.Parameters);
        Assert.Null(stmt.Option);
    }

    [Fact]
    public void Action_Bare_DoesNotSetFlag()
    {
        var stmt = SingleStatement<ActionStmt>("ACTION Approve");
        Assert.False(stmt.ExpectError);
    }

    [Fact]
    public void Action_OptionLabel_ExpectingError_SetsFlag()
    {
        var stmt = SingleStatement<ActionStmt>("ACTION Delete = \"Yes, delete\" EXPECTING ERROR");
        Assert.Equal("Delete", stmt.ActionName);
        Assert.True(stmt.ExpectError);
        var lit = Assert.IsType<LiteralExpr>(stmt.Option);
        Assert.Equal("Yes, delete", lit.Value);
    }

    [Fact]
    public void Action_NamedParams_ExpectingError_SetsFlag()
    {
        var stmt = SingleStatement<ActionStmt>("ACTION Export (Format=\"csv\") EXPECTING ERROR");
        Assert.Equal("Export", stmt.ActionName);
        Assert.True(stmt.ExpectError);
        Assert.NotNull(stmt.Parameters);
        Assert.Single(stmt.Parameters!);
    }

    [Fact]
    public void Action_DetailClause_ExpectingError_SetsFlag()
    {
        var stmt = SingleStatement<ActionStmt>("ACTION Detail \"OrderLines\" Delete EXPECTING ERROR");
        Assert.Equal("Delete", stmt.ActionName);
        Assert.Equal("OrderLines", stmt.DetailName);
        Assert.True(stmt.ExpectError);
    }

    // --- SEARCH EXPECTING ERROR ----------------------------------------------------------------

    [Fact]
    public void Search_ExpectingError_SetsFlag()
    {
        var stmt = SingleStatement<SearchStmt>("SEARCH \"Boom\" EXPECTING ERROR");
        var lit = Assert.IsType<LiteralExpr>(stmt.Text);
        Assert.Equal("Boom", lit.Value);
        Assert.True(stmt.ExpectError);
    }

    [Fact]
    public void Search_Bare_DoesNotSetFlag()
    {
        var stmt = SingleStatement<SearchStmt>("SEARCH \"Boom\"");
        Assert.False(stmt.ExpectError);
    }

    [Fact]
    public void SearchDetail_NoText_ExpectingError_SetsFlag()
    {
        // The detail form's text is optional, so EXPECTING right after the name is the suffix, not the text.
        var stmt = SingleStatement<SearchStmt>("SEARCH Detail \"Lines\" EXPECTING ERROR");
        Assert.Equal("Lines", stmt.DetailName);
        Assert.Null(stmt.Text);
        Assert.True(stmt.ExpectError);
    }

    [Fact]
    public void SearchDetail_WithText_ExpectingError_SetsFlag()
    {
        var stmt = SingleStatement<SearchStmt>("SEARCH Detail \"Lines\" \"Boom\" EXPECTING ERROR");
        Assert.Equal("Lines", stmt.DetailName);
        Assert.NotNull(stmt.Text);
        Assert.True(stmt.ExpectError);
    }

    [Fact]
    public void Search_NoText_ExpectingError_Diagnoses()
    {
        // A current-query SEARCH needs text — EXPECTING must not be swallowed as the search text.
        var diags = VidyanoScript.Lint("SEARCH EXPECTING ERROR");
        Assert.Contains(diags, d => d.Message.Contains("SEARCH needs text"));
    }

    // --- Malformed suffix (EXPECTING not followed by ERROR) ------------------------------------

    [Theory]
    [InlineData("SAVE EXPECTING")]
    [InlineData("SAVE EXPECTING FOO")]
    [InlineData("ACTION Delete EXPECTING")]
    [InlineData("ACTION Delete EXPECTING WARNING")]
    [InlineData("SEARCH Boom EXPECTING")]
    [InlineData("SEARCH Detail \"Lines\" EXPECTING FOO")]
    public void ExpectingWithoutError_Diagnoses(string body)
    {
        var diags = VidyanoScript.Lint(body);
        Assert.NotEmpty(diags);
        var combined = string.Join(" || ", diags.Select(d => $"{d.Message} | {d.Hint}"));
        Assert.Contains("ERROR", combined);
    }

    // --- Smoke: the documented forms all lint clean -------------------------------------------

    [Theory]
    [InlineData("SAVE EXPECTING ERROR")]
    [InlineData("SAVE @initial EXPECTING ERROR")]
    [InlineData("ACTION Delete EXPECTING ERROR")]
    [InlineData("ACTION Delete = \"Yes\" EXPECTING ERROR")]
    [InlineData("ACTION Detail \"Lines\" Delete EXPECTING ERROR")]
    [InlineData("SEARCH \"Boom\" EXPECTING ERROR")]
    [InlineData("SEARCH Detail \"Lines\" EXPECTING ERROR")]
    public void DocumentedForms_LintClean(string body)
    {
        AssertClean(body);
    }
}
