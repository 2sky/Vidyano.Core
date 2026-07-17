using Vidyano.Script.Diagnostics;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.IntegrationTests;

/// <summary>
/// The <c>CHART</c> verb and the <c>EXPECT Chart</c> / <c>EXPECT Chart.Data</c> subjects, run against the
/// REAL in-process Vidyano server. <see cref="ProductActions.OnChart"/> registers a <c>ByColor</c> bar chart
/// (products grouped by colour: Blue×2, Green×1, Red×1), so these tests exercise the live
/// <c>QueryFilter.Chart</c> pipeline the web client's dashboard uses — not a mock.
/// </summary>
[Collection(VidyanoAppCollection.Name)]
public sealed class ChartTests
{
    private readonly VidyanoAppFixture _app;

    public ChartTests(VidyanoAppFixture app)
    {
        _app = app;
        ShopContext.Reset();
    }

    private Task<ScriptResult> Run(string script)
    {
        var options = new VidyanoScriptOptions { Backend = _app.Backend };
        return VidyanoScript.RunAsync(script, options);
    }

    private static void AssertOk(ScriptResult result) => Assert.True(result.Ok, result.Describe());

    private static IEnumerable<Diagnostic> AllDiagnostics(ScriptResult result) =>
        result.Steps.SelectMany(s => s.Statements).SelectMany(s => s.Diagnostics);

    [Fact]
    public async Task Chart_RunsAndCapturesData()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            CHART "ByColor"
            EXPECT Chart.Data CONTAINS "ByColor"
            EXPECT Chart.Data MATCHES "\"type\":\"barchart\""
            EXPECT Chart.Data CONTAINS "Blue"
            EXPECT Chart.Data MATCHES "\"name\":\"Blue\",\"value\":2"
            """));
    }

    [Fact]
    public async Task Chart_DoesNotPushFrame_QueryStaysCurrent()
    {
        // A chart is a read-only observable — CHART must not touch the nav stack, so the Products query is
        // still current afterwards (all 4 seed products) and no PO frame was pushed.
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            EXPECT NavStack.Depth = 1
            CHART "ByColor"
            EXPECT NavStack.Depth = 1
            EXPECT NavStack.Top.Kind = "Query"
            EXPECT TotalItems = 4
            """));
    }

    [Fact]
    public async Task Chart_IsClearedByTheNextVerb()
    {
        // The capture is per-verb, like the ClientOperations buffer: after CHART, EXPECT Chart reads it, but
        // once another executable verb runs (SEARCH), the stale chart is gone.
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            EXPECT Chart IS NULL
            CHART "ByColor"
            EXPECT Chart IS NOT NULL
            SEARCH "Widget"
            EXPECT Chart IS NULL
            """));
    }

    [Fact]
    public async Task Chart_UnknownName_FailsLoudly()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            CHART "Nope"
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result),
            d => d.Kind == ErrorKind.AssertNotificationError && d.Message.Contains("Missing chart Nope"));
    }

    [Fact]
    public async Task Chart_OnDetailQuery_RunsAgainstThatDetail()
    {
        // The Detail clause runs the chart against a named detail query on the current PO — here the products
        // of category "Tools" (Widget=Blue, Gizmo=Green), so the aggregation is scoped to that detail.
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/ProductCategories
            OPEN-ROW WHERE Name = "Tools"
            CHART Detail "ProductCategory_Products" "ByColor"
            EXPECT Chart.Data MATCHES "\"type\":\"barchart\""
            EXPECT Chart.Data CONTAINS "Blue"
            """));
    }
}
