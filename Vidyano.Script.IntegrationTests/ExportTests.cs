using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.IntegrationTests;

/// <summary>
/// The built-in query exports (<c>ACTION ExportToExcel</c> / <c>ACTION ExportToCsv</c>) against the REAL
/// in-process Vidyano server. Like the web client, the runner posts them as a single action-form
/// <c>GetStream</c> (never ExecuteAction) and buffers the returned file for <c>EXPECT Stream.*</c> — so these
/// exercise the server's own Excel/CSV generation over the Products query, not a fixture.
/// </summary>
[Collection(VidyanoAppCollection.Name)]
public sealed class ExportTests
{
    private readonly VidyanoAppFixture _app;

    public ExportTests(VidyanoAppFixture app)
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

    [Fact]
    public async Task ExportToCsv_DownloadsTheQueryRows()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            ACTION ExportToCsv
            EXPECT Stream.Name = "Products.csv"
            EXPECT Stream.Length > 0
            EXPECT Stream.Text CONTAINS "Widget"
            EXPECT Stream.Text CONTAINS "Gadget"
            EXPECT Stream.Text CONTAINS "Gizmo"
            """));
    }

    [Fact]
    public async Task ExportToExcel_DownloadsAWorkbook()
    {
        // An .xlsx is a zip package, so the decoded text starts with the "PK" local-file-header magic.
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            ACTION ExportToExcel
            EXPECT Stream.Name = "Products.xlsx"
            EXPECT Stream.Length > 0
            EXPECT Stream.Text MATCHES "^PK"
            """));
    }

    [Fact]
    public async Task Export_DetailQuery_PostsItsParent()
    {
        // A detail query only resolves against its owning PO (ProductCategory_Products calls EnsureParent), so
        // this pins that the export posts the action's parent alongside the query. Tools holds Widget + Gizmo.
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/ProductCategories
            OPEN-ROW WHERE Name = "Tools"
            SEARCH Detail "ProductCategory_Products"
            ACTION Detail "ProductCategory_Products" ExportToCsv
            EXPECT Stream.Text CONTAINS "Widget"
            EXPECT Stream.Text CONTAINS "Gizmo"
            EXPECT Stream.Text NOT CONTAINS "Gadget"
            """));
    }

    [Fact]
    public async Task Export_DoesNotPushFrame_AndIsClearedByTheNextVerb()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            ACTION ExportToCsv
            EXPECT NavStack.Depth = 1
            EXPECT NavStack.Top.Kind = "Query"
            EXPECT Stream IS NOT NULL
            OPEN-ROW WHERE Name = "Widget"
            EXPECT Stream IS NULL
            """));
    }
}
