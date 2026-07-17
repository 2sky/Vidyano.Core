using System.Collections.Generic;
using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Parsing;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Parser/grammar coverage for the <c>CHART</c> verb and the <c>EXPECT Chart</c> / <c>EXPECT Chart.Data</c>
/// subjects. The live capture round-trip (running <c>QueryFilter.Chart</c> and reading back the JSON) needs a
/// real server and is covered by <c>Vidyano.Script.IntegrationTests.ChartTests</c>; these are server-free
/// parse + lint checks.
/// </summary>
public sealed class ChartLintTests
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
    public void Chart_ParsesQuotedName()
    {
        var stmt = SingleStatement<ChartStmt>("CHART \"ByColor\"");
        Assert.Null(stmt.DetailName);
        var lit = Assert.IsType<LiteralExpr>(stmt.ChartName);
        Assert.Equal("ByColor", lit.Value);
    }

    [Fact]
    public void Chart_ParsesBareIdentifierName()
    {
        // Like ACTION Approve, an unquoted single-word chart name is accepted (value expression).
        var stmt = SingleStatement<ChartStmt>("CHART SessionsHistory");
        Assert.IsType<IdentifierExpr>(stmt.ChartName);
    }

    [Fact]
    public void Chart_ParsesDetailClause()
    {
        var stmt = SingleStatement<ChartStmt>("CHART Detail \"Sessions\" \"SessionsHistory\"");
        Assert.Equal("Sessions", stmt.DetailName);
        var lit = Assert.IsType<LiteralExpr>(stmt.ChartName);
        Assert.Equal("SessionsHistory", lit.Value);
    }

    [Fact]
    public void Chart_ParsesInterpolatedName()
    {
        var stmt = SingleStatement<ChartStmt>("CHART \"{{name}}\"");
        Assert.IsType<StringInterpExpr>(stmt.ChartName);
    }

    [Fact]
    public void Chart_WithNoName_IsRejected()
    {
        var diags = Lint("CHART");
        Assert.NotEmpty(diags);
    }

    [Fact]
    public void Chart_IsARecognizedVerb()
    {
        // Regression against a missing dispatch/catalog entry: CHART must not lint as an unknown verb.
        var diags = Lint("CHART \"ByColor\"");
        Assert.DoesNotContain(diags, d => d.Message.StartsWith("Unknown verb", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Chart_IsInVerbCatalog()
    {
        Assert.True(VerbCatalog.TryGet("CHART", out var info));
        Assert.Equal("CHART", info!.Name);
        Assert.NotEmpty(info.Examples);
    }

    [Fact]
    public void ExpectChart_ParsesAsChartSubject()
    {
        var stmt = SingleStatement<ExpectStmt>("EXPECT Chart IS NULL");
        Assert.Equal(ExpectSubjectKind.Chart, stmt.Subject.Kind);
        Assert.Equal(ExpectOp.IsNull, stmt.Op);
    }

    [Fact]
    public void ExpectChartData_ParsesAsChartSubject()
    {
        var stmt = SingleStatement<ExpectStmt>("EXPECT Chart.Data CONTAINS \"barchart\"");
        Assert.Equal(ExpectSubjectKind.Chart, stmt.Subject.Kind);
        Assert.Equal(ExpectOp.Contains, stmt.Op);
    }

    [Fact]
    public void ExpectChart_UnknownProperty_IsRejected()
    {
        var diags = Lint("EXPECT Chart.Bogus = \"x\"");
        Assert.NotEmpty(diags);
        Assert.Contains(diags, d => d.Message.Contains("Chart has no property"));
    }
}
