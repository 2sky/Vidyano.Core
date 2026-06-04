using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Parsing;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Parser/grammar coverage for the server RetryAction surface: the <c>CONFIRM</c> verb that answers an
/// open retry dialog and the <c>EXPECT RetryDialog.*</c> subjects that read it. These assert the parse
/// shape only — the parking coroutine that pauses an ACTION/SAVE and resumes it on CONFIRM needs a live
/// server (a real <see cref="VidyanoSession"/> drives a real Client, there is no mock), so its runtime
/// behavior is exercised separately. Lint-only, mirroring <see cref="ExpectingErrorLintTests"/>.
/// </summary>
public sealed class RetryActionLintTests
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

    // --- CONFIRM ------------------------------------------------------------------------------

    [Fact]
    public void Confirm_Label_ParsesWithNoHint()
    {
        var stmt = SingleStatement<ConfirmStmt>("CONFIRM \"Yes\"");
        Assert.Null(stmt.OptionHint);
        var lit = Assert.IsType<LiteralExpr>(stmt.Option);
        Assert.Equal("Yes", lit.Value);
    }

    [Fact]
    public void Confirm_ById_SetsRawIdHint()
    {
        var stmt = SingleStatement<ConfirmStmt>("CONFIRM ID 0");
        Assert.Equal(ReferenceHintKind.RawId, stmt.OptionHint);
        var lit = Assert.IsType<LiteralExpr>(stmt.Option);
        Assert.Equal(0L, lit.Value);
    }

    [Fact]
    public void Confirm_Interpolated_KeepsExpression()
    {
        var stmt = SingleStatement<ConfirmStmt>("CONFIRM {{@choice}}");
        Assert.Null(stmt.OptionHint);
        Assert.IsType<InterpExpr>(stmt.Option);
    }

    [Theory]
    [InlineData("CONFIRM")]
    [InlineData("CONFIRM ID")]
    public void Confirm_MissingValue_Diagnoses(string body)
    {
        var diags = VidyanoScript.Lint(body);
        Assert.NotEmpty(diags);
    }

    // --- EXPECT RetryDialog.* ------------------------------------------------------------------

    [Theory]
    [InlineData("EXPECT RetryDialog.Title = \"Are you sure?\"", ExpectSubjectKind.RetryTitle)]
    [InlineData("EXPECT RetryDialog.Message = \"This cannot be undone.\"", ExpectSubjectKind.RetryMessage)]
    [InlineData("EXPECT RetryDialog.Options = \"Yes, No\"", ExpectSubjectKind.RetryOptions)]
    public void ExpectRetryDialog_ResolvesSubjectKind(string body, ExpectSubjectKind expected)
    {
        var stmt = SingleStatement<ExpectStmt>(body);
        Assert.Equal(expected, stmt.Subject.Kind);
    }

    [Fact]
    public void ExpectRetryDialog_IsNull_LintsClean()
    {
        // The natural "no dialog open" assertion — RetryDialog.* is null when nothing is parked.
        AssertClean("EXPECT RetryDialog.Title IS NULL");
    }

    [Theory]
    [InlineData("EXPECT RetryDialog")]            // missing leaf
    [InlineData("EXPECT RetryDialog.Bogus = 1")]  // unknown leaf
    public void ExpectRetryDialog_Malformed_Diagnoses(string body)
    {
        var diags = VidyanoScript.Lint(body);
        Assert.NotEmpty(diags);
        Assert.Contains(diags, d => d.Message.Contains("RetryDialog"));
    }

    // --- Smoke: the documented forms all lint clean -------------------------------------------

    [Theory]
    [InlineData("CONFIRM \"Yes\"")]
    [InlineData("CONFIRM ID 0")]
    [InlineData("EXPECT RetryDialog.Title = \"x\"")]
    [InlineData("EXPECT RetryDialog.Message MATCHES \".+\"")]
    [InlineData("EXPECT RetryDialog.Options CONTAINS \"Yes\"")]
    public void DocumentedForms_LintClean(string body)
    {
        AssertClean(body);
    }
}
