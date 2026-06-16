using System.Collections.Generic;
using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Parsing;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Parser/grammar coverage for <c>EXPECT &lt;ref&gt; = ID "&lt;id&gt;"</c> — asserting a reference attribute by
/// its document id, symmetric with the existing <c>SET &lt;ref&gt; = ID "&lt;id&gt;"</c>. The runtime
/// ObjectId-vs-display selection needs a live PO, so it is covered downstream/by the samples; these are
/// server-free parse + lint checks.
/// </summary>
public sealed class ExpectByIdLintTests
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
    public void ExpectEqualsId_ParsesAsAttributeWithRawIdHint()
    {
        var stmt = SingleStatement<ExpectStmt>("EXPECT Customer = ID \"people/acme\"");
        Assert.Equal(ExpectSubjectKind.Attribute, stmt.Subject.Kind);
        Assert.Equal("Customer", stmt.Subject.Name);
        Assert.Equal(ReferenceHintKind.RawId, stmt.Subject.Hint);
        Assert.Equal(ExpectOp.Eq, stmt.Op);
        var lit = Assert.IsType<LiteralExpr>(stmt.Value);
        Assert.Equal("people/acme", lit.Value);
    }

    [Fact]
    public void ExpectNotEqualsId_ParsesWithRawIdHintAndNotEq()
    {
        var stmt = SingleStatement<ExpectStmt>("EXPECT Customer != ID \"people/acme\"");
        Assert.Equal(ReferenceHintKind.RawId, stmt.Subject.Hint);
        Assert.Equal(ExpectOp.NotEq, stmt.Op);
    }

    [Fact]
    public void RequiresEqualsId_InheritsTheRawIdHint()
    {
        // REQUIRES reuses the EXPECT assertion grammar, so the hint rides through on the shared subject.
        var stmt = SingleStatement<RequiresStmt>("REQUIRES Customer = ID \"people/acme\"");
        Assert.Equal(ExpectSubjectKind.Attribute, stmt.Subject.Kind);
        Assert.Equal(ReferenceHintKind.RawId, stmt.Subject.Hint);
    }

    [Fact]
    public void ExpectEqualsId_OnArbitraryAttributeName_LintsClean()
    {
        // The parser doesn't know whether Customer is a reference — that's a runtime concern. The form
        // itself is well-formed on any attribute name.
        var diags = Lint("EXPECT Whatever = ID \"x\"");
        Assert.Empty(diags);
    }

    [Fact]
    public void ExpectEqualsDisplay_StillCarriesNoHint()
    {
        // Regression: the plain display-value comparison is unchanged (no hint, compares attr.Value).
        var stmt = SingleStatement<ExpectStmt>("EXPECT Status = \"Approved\"");
        Assert.Null(stmt.Subject.Hint);
    }

    [Fact]
    public void ExpectId_OnNonAttributeSubject_IsRejected()
    {
        var diags = Lint("EXPECT TotalItems = ID \"5\"");
        Assert.NotEmpty(diags);
        Assert.Contains(diags, d => d.Message.Contains("reference attribute"));
    }

    [Fact]
    public void ExpectId_WithOrderingOperator_IsRejected()
    {
        // A document id has no ordering — only = / != accept the ID keyword.
        var diags = Lint("EXPECT Customer < ID \"x\"");
        Assert.NotEmpty(diags);
    }
}
