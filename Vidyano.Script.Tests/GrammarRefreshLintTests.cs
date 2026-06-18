using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Parsing;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Parser/grammar coverage for the grammar refresh: the <c>ACTION X = …</c> form (positional
/// option pick), <c>EXPECT Attribute X IS AVAILABLE</c>, <c>EXPECT Detail "X" IS [NOT] AVAILABLE | VISIBLE</c>,
/// and the documented-but-already-working <c>SET attr = LOOKUP|ID|null</c> shapes on any attribute name.
/// These tests never touch a server: lint-only assertions plus a few direct AST-shape checks that go
/// through the public <see cref="Parser"/> the same way <see cref="StringInterpolationTests"/> does.
/// </summary>
public sealed class GrammarRefreshLintTests
{
    private static void AssertClean(string body)
    {
        var diags = VidyanoScript.Lint(body);
        Assert.True(diags.Count == 0,
            $"Expected no diagnostics, got: {string.Join("; ", diags.Select(d => $"{d.Kind}: {d.Message}"))}");
    }

    private static void AssertHasDiagnostic(string body)
    {
        var diags = VidyanoScript.Lint(body);
        Assert.True(diags.Count > 0, "Expected at least one parse diagnostic, got none.");
    }

    private static ScriptAst ParseClean(string body)
    {
        var lexer = new Lexer(body, "<test>");
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens, lexer.Diagnostics);
        var ast = parser.Parse();
        Assert.True(parser.Diagnostics.Count == 0,
            $"Parse errors: {string.Join("; ", parser.Diagnostics.Select(d => d.Message))}");
        return ast;
    }

    private static T SingleStatement<T>(string body) where T : Statement
    {
        var ast = ParseClean(body);
        var stmts = ast.Steps.SelectMany(s => s.Statements).ToList();
        Assert.Single(stmts);
        return Assert.IsType<T>(stmts[0]);
    }

    // --- ACTION X = "label" / = ID 0 (positional option pick) ---------------------------------

    [Fact]
    public void Action_EqualsLabel_ParsesAsOptionWithNoHint()
    {
        var stmt = SingleStatement<ActionStmt>("ACTION Delete = \"Yes, delete\"");
        Assert.Equal("Delete", stmt.ActionName);
        Assert.Null(stmt.Parameters);
        Assert.Null(stmt.OptionHint);
        var lit = Assert.IsType<LiteralExpr>(stmt.Option);
        Assert.Equal("Yes, delete", lit.Value);
    }

    [Fact]
    public void Action_EqualsIdInteger_ParsesAsOptionWithRawIdHint()
    {
        var stmt = SingleStatement<ActionStmt>("ACTION Delete = ID 0");
        Assert.Equal("Delete", stmt.ActionName);
        Assert.Null(stmt.Parameters);
        Assert.Equal(ReferenceHintKind.RawId, stmt.OptionHint);
        var lit = Assert.IsType<LiteralExpr>(stmt.Option);
        // Numeric literals come through as long from the lexer.
        Assert.Equal(0L, lit.Value);
    }

    [Fact]
    public void Action_EqualsIdString_ParsesAsRawIdHintWithStringLiteral()
    {
        // The parser's job ends at hint=RawId + value=string; runtime decides what to do with it.
        // Documenting the actual shape here so any future coercion change shows up as a failure.
        var stmt = SingleStatement<ActionStmt>("ACTION Delete = ID \"0\"");
        Assert.Equal("Delete", stmt.ActionName);
        Assert.Null(stmt.Parameters);
        Assert.Equal(ReferenceHintKind.RawId, stmt.OptionHint);
        var lit = Assert.IsType<LiteralExpr>(stmt.Option);
        Assert.Equal("0", lit.Value);
    }

    // --- ACTION combo rejection (= and (…) are mutually exclusive) ----------------------------

    [Fact]
    public void Action_EqualsThenParenParams_Diagnoses()
    {
        var diags = VidyanoScript.Lint("ACTION X = \"label\" (Foo=1)");
        Assert.NotEmpty(diags);
        // The implementation emits a hint that names both forms; assert the hint surfaces them.
        var combined = string.Join(" || ", diags.Select(d => $"{d.Message} | {d.Hint}"));
        Assert.Contains("ACTION X = \"label\"", combined);
        Assert.Contains("ACTION X (Param", combined);
    }

    [Fact]
    public void Action_ParenParamsThenEquals_Diagnoses()
    {
        // Symmetric to the forward-direction rejection: ACTION X (Foo=1) = "label" is the same
        // illegal combo and must produce the same hint citing both forms. Earlier behavior silently
        // dropped the trailing `= "label"` via SkipToEndOfLine — a footgun where a confused author
        // would ship a script whose Execute call didn't carry the option they typed.
        var diags = VidyanoScript.Lint("ACTION X (Foo=1) = \"label\"");
        Assert.NotEmpty(diags);
        var combined = string.Join(" || ", diags.Select(d => $"{d.Message} | {d.Hint}"));
        Assert.Contains("ACTION X = \"label\"", combined);
        Assert.Contains("ACTION X (Param", combined);
    }

    [Fact]
    public void Action_EqualsNull_Diagnoses()
    {
        // ACTION X = null is a footgun: the `= …` clause was written (so the author meant
        // *something*), but a null option value silently degrades to bare ACTION X at the runtime
        // boundary. The Interpreter catches it while it still knows `a.Option` was syntactically
        // present, before handing off to the session overload that can't tell those cases apart.
        // Lint-only smoke can confirm parsing succeeds — the diagnostic fires at runtime.
        var stmt = SingleStatement<ActionStmt>("ACTION X = null");
        Assert.Equal("X", stmt.ActionName);
        Assert.Null(stmt.Parameters);
        Assert.Null(stmt.OptionHint);
        var lit = Assert.IsType<LiteralExpr>(stmt.Option);
        Assert.Null(lit.Value);
    }

    // --- ACTION named-params back-compat -------------------------------------------------------

    [Fact]
    public void Action_NamedParams_StillParses()
    {
        var stmt = SingleStatement<ActionStmt>("ACTION Export (Format=\"csv\")");
        Assert.Equal("Export", stmt.ActionName);
        Assert.Null(stmt.Option);
        Assert.Null(stmt.OptionHint);
        Assert.NotNull(stmt.Parameters);
        Assert.Single(stmt.Parameters!);
        var pv = Assert.IsType<LiteralExpr>(stmt.Parameters!["Format"]);
        Assert.Equal("csv", pv.Value);
    }

    [Fact]
    public void Action_BareNoArgs_StillParses()
    {
        // Regression smoke: the simplest form must keep working after the `=` branch was added.
        var stmt = SingleStatement<ActionStmt>("ACTION Approve");
        Assert.Equal("Approve", stmt.ActionName);
        Assert.Null(stmt.Option);
        Assert.Null(stmt.OptionHint);
        Assert.Null(stmt.Parameters);
    }

    // --- EXPECT Attribute X IS AVAILABLE -------------------------------------------------------

    [Fact]
    public void ExpectAttribute_IsAvailable_ParsesAsAttributeFlagAvailable()
    {
        var stmt = SingleStatement<ExpectStmt>("EXPECT Attribute Name IS AVAILABLE");
        Assert.Equal(ExpectSubjectKind.AttributeFlag, stmt.Subject.Kind);
        Assert.Equal("Name", stmt.Subject.Name);
        Assert.Equal(AttributeFlagKind.Available, stmt.Subject.Flag);
        Assert.Equal(ExpectOp.Is, stmt.Op);
    }

    [Fact]
    public void ExpectAttribute_IsNotAvailable_ParsesAsAttributeFlagAvailableWithIsNot()
    {
        var stmt = SingleStatement<ExpectStmt>("EXPECT Attribute Name IS NOT AVAILABLE");
        Assert.Equal(ExpectSubjectKind.AttributeFlag, stmt.Subject.Kind);
        Assert.Equal("Name", stmt.Subject.Name);
        Assert.Equal(AttributeFlagKind.Available, stmt.Subject.Flag);
        Assert.Equal(ExpectOp.IsNot, stmt.Op);
    }

    [Theory]
    [InlineData("EXPECT Attribute Name IS VISIBLE")]
    [InlineData("EXPECT Attribute Name IS READONLY")]
    [InlineData("EXPECT Attribute Name IS REQUIRED")]
    [InlineData("EXPECT Attribute Name IS AVAILABLE")]
    public void ExpectAttribute_AllAllowedFlags_Parse(string body)
    {
        AssertClean(body);
    }

    [Fact]
    public void ExpectAction_IsRequired_Rejected()
    {
        // REQUIRED only makes sense for attributes; the per-subject allow-list still rejects it on Action.
        var diags = VidyanoScript.Lint("EXPECT Action Approve IS REQUIRED");
        Assert.NotEmpty(diags);
        var combined = string.Join(" || ", diags.Select(d => $"{d.Message} | {d.Hint}"));
        // Hint should steer the user toward the legal Action flags.
        Assert.Contains("AVAILABLE", combined);
    }

    // --- EXPECT Detail "X" IS [NOT] AVAILABLE | VISIBLE ----------------------------------------

    [Fact]
    public void ExpectDetail_IsAvailable_ParsesAsDetailQueryFlag()
    {
        var stmt = SingleStatement<ExpectStmt>("EXPECT Detail \"OrderLines\" IS AVAILABLE");
        Assert.Equal(ExpectSubjectKind.DetailQueryFlag, stmt.Subject.Kind);
        Assert.Equal("OrderLines", stmt.Subject.DetailName);
        Assert.Equal(AttributeFlagKind.Available, stmt.Subject.Flag);
        Assert.Equal(ExpectOp.Is, stmt.Op);
    }

    [Fact]
    public void ExpectDetail_IsNotVisible_ParsesAsDetailQueryFlagWithIsNot()
    {
        var stmt = SingleStatement<ExpectStmt>("EXPECT Detail \"OrderLines\" IS NOT VISIBLE");
        Assert.Equal(ExpectSubjectKind.DetailQueryFlag, stmt.Subject.Kind);
        Assert.Equal("OrderLines", stmt.Subject.DetailName);
        Assert.Equal(AttributeFlagKind.Visible, stmt.Subject.Flag);
        Assert.Equal(ExpectOp.IsNot, stmt.Op);
    }

    [Fact]
    public void ExpectDetail_BareIdentifierName_IsAvailable_Parses()
    {
        AssertClean("EXPECT Detail OrderLines IS AVAILABLE");
    }

    [Fact]
    public void ExpectDetail_TotalItems_StillParses()
    {
        // Regression smoke: the existing query-family redirect form is unaffected by the new IS-flag form.
        var stmt = SingleStatement<ExpectStmt>("EXPECT Detail \"OrderLines\" TotalItems = 3");
        Assert.Equal(ExpectSubjectKind.TotalItems, stmt.Subject.Kind);
        Assert.Equal("OrderLines", stmt.Subject.DetailName);
        Assert.Equal(ExpectOp.Eq, stmt.Op);
    }

    [Fact]
    public void ExpectDetail_IsRequired_Rejected()
    {
        var diags = VidyanoScript.Lint("EXPECT Detail \"OrderLines\" IS REQUIRED");
        Assert.NotEmpty(diags);
        var combined = string.Join(" || ", diags.Select(d => $"{d.Message} | {d.Hint}"));
        Assert.Contains("AVAILABLE", combined);
        Assert.Contains("VISIBLE", combined);
    }

    // --- EXPECT Detail "X" Action Y IS [NOT] AVAILABLE | VISIBLE --------------------------------
    // Symmetric with ACTION Detail "X" Y (execution) and EXPECT Action Y (current PO/query): assert a
    // named action's gating on a detail (sub-)query. Parse-shape only; the runtime — resolving the
    // action against CurrentPo.Queries[DetailName] alone, not the master PO — needs a live server and
    // is verified downstream, exactly like EXPECT Action (see ExpectActionAbsentLintTests).

    [Theory]
    [InlineData("EXPECT Detail \"OrderLines\" Action Delete IS NOT AVAILABLE", AttributeFlagKind.Available, ExpectOp.IsNot)]
    [InlineData("EXPECT Detail \"OrderLines\" Action Delete IS AVAILABLE", AttributeFlagKind.Available, ExpectOp.Is)]
    [InlineData("EXPECT Detail \"OrderLines\" Action Delete IS NOT VISIBLE", AttributeFlagKind.Visible, ExpectOp.IsNot)]
    [InlineData("EXPECT Detail \"OrderLines\" Action Delete IS VISIBLE", AttributeFlagKind.Visible, ExpectOp.Is)]
    public void ExpectDetailAction_FlagForms_ParseToExpectedShape(string body, AttributeFlagKind flag, ExpectOp op)
    {
        var stmt = SingleStatement<ExpectStmt>(body);
        Assert.Equal(ExpectSubjectKind.Action, stmt.Subject.Kind);
        Assert.Equal("Delete", stmt.Subject.Name);
        Assert.Equal("OrderLines", stmt.Subject.DetailName);
        Assert.Equal(flag, stmt.Subject.Flag);
        Assert.Equal(op, stmt.Op);
    }

    [Theory]
    [InlineData("EXPECT Detail \"OrderLines\" Action Delete IS NOT AVAILABLE")]
    [InlineData("EXPECT Detail \"OrderLines\" Action New IS VISIBLE")]
    [InlineData("EXPECT Detail OrderLines Action New IS AVAILABLE")] // bare-identifier detail name
    public void ExpectDetailAction_FlagForms_LintClean(string body)
    {
        AssertClean(body);
    }

    [Fact]
    public void ExpectDetailAction_IsRequired_Rejected()
    {
        // The Action flag allow-list still applies through the Detail clause: REQUIRED is attribute-only.
        var diags = VidyanoScript.Lint("EXPECT Detail \"OrderLines\" Action Delete IS REQUIRED");
        Assert.NotEmpty(diags);
        var combined = string.Join(" || ", diags.Select(d => $"{d.Message} | {d.Hint}"));
        Assert.Contains("AVAILABLE", combined);
    }

    [Fact]
    public void ExpectDetailAction_DisplayName_Rejected()
    {
        // DISPLAY-NAME is out of scope for the Detail clause — only the AVAILABLE/VISIBLE gating forms
        // are permitted, so an ActionDisplayName inner subject is rejected.
        AssertHasDiagnostic("EXPECT Detail \"OrderLines\" Action Delete DISPLAY-NAME = \"Remove\"");
    }

    [Fact]
    public void ExpectDetail_NonQueryNonActionInner_RejectedWithUpdatedMessage()
    {
        // A subject that is neither query-family nor Action (here Notification) is still rejected, and
        // the message now advertises the Action option alongside the query subjects.
        var diags = VidyanoScript.Lint("EXPECT Detail \"OrderLines\" Notification");
        Assert.NotEmpty(diags);
        var combined = string.Join(" || ", diags.Select(d => $"{d.Message} | {d.Hint}"));
        Assert.Contains("an Action", combined);
    }

    // --- SET attr = LOOKUP | ID | null ---------------------------------------------------------

    [Fact]
    public void Set_Lookup_ParsesWithLookupHint()
    {
        var stmt = SingleStatement<SetStmt>("SET Status = LOOKUP \"Active\"");
        Assert.Equal("Status", stmt.Attribute);
        Assert.Equal(ReferenceHintKind.Lookup, stmt.Hint);
        var lit = Assert.IsType<LiteralExpr>(stmt.Value);
        Assert.Equal("Active", lit.Value);
    }

    [Fact]
    public void Set_Id_ParsesWithRawIdHint()
    {
        var stmt = SingleStatement<SetStmt>("SET Status = ID \"active\"");
        Assert.Equal("Status", stmt.Attribute);
        Assert.Equal(ReferenceHintKind.RawId, stmt.Hint);
        var lit = Assert.IsType<LiteralExpr>(stmt.Value);
        Assert.Equal("active", lit.Value);
    }

    [Fact]
    public void Set_Null_ParsesAsNullLiteralWithNoHint()
    {
        var stmt = SingleStatement<SetStmt>("SET Status = null");
        Assert.Equal("Status", stmt.Attribute);
        Assert.Null(stmt.Hint);
        var lit = Assert.IsType<LiteralExpr>(stmt.Value);
        Assert.Null(lit.Value);
    }

    [Fact]
    public void Set_LookupOnArbitraryName_LintsClean()
    {
        // The parser doesn't know whether Status is a reference, KeyValueList, or anything else —
        // that's a runtime concern. Lint-only smoke test that all three shapes are accepted on a
        // fresh attribute name without further context.
        AssertClean("SET Whatever = LOOKUP \"x\"");
        AssertClean("SET Whatever = ID \"x\"");
        AssertClean("SET Whatever = null");
    }
}
