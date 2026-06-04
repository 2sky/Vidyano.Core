using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Parsing;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Grammar coverage for <c>EXPECT Action X IS [NOT] AVAILABLE | VISIBLE</c> — the forms that let a
/// script assert an action was filtered out of a PersistentObject (e.g. removed server-side via
/// <c>DisableActions</c>). These assert the parse shape only: an absent action is resolved against
/// <c>Current.CurrentPo</c>, which is populated from server JSON, so the runtime behavior — an absent
/// action answering <c>false</c> for an explicit flag (passing <c>IS NOT AVAILABLE</c>, failing the
/// assertion on <c>IS AVAILABLE</c>) while a flagless form still errors with <c>ResolveAction</c> to
/// preserve typo detection — needs a live server and is verified downstream. Lint-only, mirroring
/// <see cref="RetryActionLintTests"/>.
/// </summary>
public sealed class ExpectActionAbsentLintTests
{
    private static void AssertClean(string body)
    {
        var diags = VidyanoScript.Lint(body);
        Assert.True(diags.Count == 0,
            $"Expected no diagnostics, got: {string.Join("; ", diags.Select(d => $"{d.Kind}: {d.Message}"))}");
    }

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

    // The (Flag, Op) shape is the contract the runtime branch depends on: Flag picks the property
    // (CanExecute / IsVisible), Op carries the IS / IS NOT negation that turns an absent action's
    // `false` into a pass or an assertion failure.
    [Theory]
    [InlineData("EXPECT Action Delete IS AVAILABLE", AttributeFlagKind.Available, ExpectOp.Is)]
    [InlineData("EXPECT Action Delete IS NOT AVAILABLE", AttributeFlagKind.Available, ExpectOp.IsNot)]
    [InlineData("EXPECT Action Delete IS VISIBLE", AttributeFlagKind.Visible, ExpectOp.Is)]
    [InlineData("EXPECT Action Delete IS NOT VISIBLE", AttributeFlagKind.Visible, ExpectOp.IsNot)]
    public void ActionFlagForms_ParseToExpectedShape(string body, AttributeFlagKind flag, ExpectOp op)
    {
        var stmt = SingleExpect(body);
        Assert.Equal(ExpectSubjectKind.Action, stmt.Subject.Kind);
        Assert.Equal("Delete", stmt.Subject.Name);
        Assert.Equal(flag, stmt.Subject.Flag);
        Assert.Equal(op, stmt.Op);
    }

    [Theory]
    [InlineData("EXPECT Action Delete IS AVAILABLE")]
    [InlineData("EXPECT Action Delete IS NOT AVAILABLE")]
    [InlineData("EXPECT Action Delete IS VISIBLE")]
    [InlineData("EXPECT Action Delete IS NOT VISIBLE")]
    public void ActionFlagForms_LintClean(string body)
    {
        AssertClean(body);
    }
}
