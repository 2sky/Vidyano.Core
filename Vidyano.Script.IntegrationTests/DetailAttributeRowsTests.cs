using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.IntegrationTests;

/// <summary>
/// <c>DELETE-ROW Detail Attribute</c> and <c>EXPECT Detail Attribute</c> against the REAL in-process server. The
/// <c>Products</c> AsDetail attribute on ProductCategory is backed by the <c>ProductCategory_Products</c> details
/// query, so removing a row and saving exercises the server's <c>DeletedObjects</c> path end to end.
/// Seed: category "Tools" holds Gizmo and Widget (server row order).
/// </summary>
[Collection(VidyanoAppCollection.Name)]
public sealed class DetailAttributeRowsTests
{
    private readonly VidyanoAppFixture _app;

    public DetailAttributeRowsTests(VidyanoAppFixture app)
    {
        _app = app;
        ShopContext.Reset();
    }

    private Task<ScriptResult> Run(string script) =>
        VidyanoScript.RunAsync(script, new VidyanoScriptOptions { Backend = _app.Backend });

    private const string OpenTools = """
        SIGN-IN admin / admin
        OPEN MenuItem Home/ProductCategories
        OPEN-ROW WHERE Name = "Tools"
        """;

    [Fact]
    public async Task Rows_AreCountedAndReadable()
    {
        var result = await Run(OpenTools + """

            EXPECT Detail Attribute "Products" TotalItems = 2
            EXPECT Detail Attribute "Products" ROW 0 Name = "Gizmo"
            EXPECT Detail Attribute "Products" ROW 1 Name = "Widget"
            """);
        Assert.True(result.Ok, result.Describe());
    }

    [Fact]
    public async Task DeleteRow_ThenSave_RemovesTheRowOnTheServer()
    {
        var result = await Run(OpenTools + """

            EDIT
            DELETE-ROW Detail Attribute "Products" WHERE Name = "Widget"
            EXPECT Detail Attribute "Products" TotalItems = 1
            EXPECT Detail Attribute "Products" ROW 0 Name = "Gizmo"
            SAVE
            OPEN MenuItem Home/Products
            EXPECT TotalItems = 3
            """);
        Assert.True(result.Ok, result.Describe());
    }

    [Fact]
    public async Task DeleteRow_ByIndex_Works()
    {
        var result = await Run(OpenTools + """

            EDIT
            DELETE-ROW Detail Attribute "Products" 1
            EXPECT Detail Attribute "Products" TotalItems = 1
            EXPECT Detail Attribute "Products" ROW 0 Name = "Gizmo"
            """);
        Assert.True(result.Ok, result.Describe());
    }

    [Fact]
    public async Task AddRow_SetCells_ThenSave_CreatesTheRowOnTheServer()
    {
        var result = await Run(OpenTools + """

            EDIT
            ADD-ROW Detail Attribute "Products" AS @new
            EXPECT {{new}} = 2
            EXPECT Detail Attribute "Products" TotalItems = 3
            SET Detail Attribute "Products" ROW {{new}} Name = "Sprocket"
            EXPECT Detail Attribute "Products" ROW {{new}} Name = "Sprocket"
            EXPECT IsDirty = true
            SAVE
            OPEN MenuItem Home/Products
            EXPECT TotalItems = 5
            OPEN-ROW WHERE Name = "Sprocket"
            """);
        Assert.True(result.Ok, result.Describe());
    }

    [Fact]
    public async Task SetRowCell_OnExistingRow_MarksTheParentDirty()
    {
        var result = await Run(OpenTools + """

            EDIT
            SET Detail Attribute "Products" ROW 0 Color = "Purple"
            EXPECT Detail Attribute "Products" ROW 0 Color = "Purple"
            EXPECT IsDirty = true
            """);
        Assert.True(result.Ok, result.Describe());
    }

    [Fact]
    public async Task DeleteRow_OfANewRow_DropsIt()
    {
        var result = await Run(OpenTools + """

            EDIT
            ADD-ROW Detail Attribute "Products"
            DELETE-ROW Detail Attribute "Products" 2
            EXPECT Detail Attribute "Products" TotalItems = 2
            """);
        Assert.True(result.Ok, result.Describe());
    }

    [Fact]
    public async Task AddRow_OutsideEdit_IsRefused()
    {
        var result = await Run(OpenTools + """

            ADD-ROW Detail Attribute "Products"
            """);
        Assert.False(result.Ok);
        Assert.Contains("edit mode", result.Describe());
    }

    [Fact]
    public async Task DeleteRow_OutsideEdit_IsRefused()
    {
        var result = await Run(OpenTools + """

            DELETE-ROW Detail Attribute "Products" 0
            """);
        Assert.False(result.Ok);
        Assert.Contains("edit mode", result.Describe());
    }

    [Fact]
    public async Task DeleteRow_UnknownRow_Fails()
    {
        var result = await Run(OpenTools + """

            EDIT
            DELETE-ROW Detail Attribute "Products" WHERE Name = "Nope"
            """);
        Assert.False(result.Ok);
    }

    [Fact]
    public async Task NonDetailAttribute_IsRefused()
    {
        var result = await Run(OpenTools + """

            EXPECT Detail Attribute "Name" TotalItems = 1
            """);
        Assert.False(result.Ok);
        Assert.Contains("not a detail attribute", result.Describe());
    }
}
