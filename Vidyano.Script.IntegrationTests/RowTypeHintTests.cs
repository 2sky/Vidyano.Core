using Vidyano.Script.Diagnostics;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.IntegrationTests;

/// <summary>
/// <c>EXPECT Row [Column X] TYPEHINT key</c> against the REAL in-process server: <see cref="ProductActions.QueryExecuted"/>
/// sets <c>foreground=red</c> on a red product's row and <c>background=pink</c> on its Color cell.
/// </summary>
[Collection(VidyanoAppCollection.Name)]
public sealed class RowTypeHintTests
{
    private readonly VidyanoAppFixture _app;

    public RowTypeHintTests(VidyanoAppFixture app)
    {
        _app = app;
        ShopContext.Reset();
    }

    private Task<ScriptResult> Run(string script) =>
        VidyanoScript.RunAsync(script, new VidyanoScriptOptions { Backend = _app.Backend });

    private static IEnumerable<Diagnostic> AllDiagnostics(ScriptResult result) =>
        result.Steps.SelectMany(s => s.Statements).SelectMany(s => s.Diagnostics);

    [Fact]
    public async Task HintedRow_ReadsItsRowAndCellHints()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            SELECT-ROWS WHERE Name = "Gadget"
            EXPECT Row TYPEHINT foreground = "red"
            EXPECT Row TYPEHINT Foreground = "red"
            EXPECT Row Column Color TYPEHINT background = "pink"
            EXPECT Row Column Name TYPEHINT background IS NULL
            EXPECT Row TYPEHINT background IS NULL
            """);
        Assert.True(result.Ok, result.Describe());
    }

    [Fact]
    public async Task UnhintedRow_ReadsNull()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            SELECT-ROWS WHERE Name = "Widget"
            EXPECT Row TYPEHINT foreground IS NULL
            EXPECT Row Column Color TYPEHINT background IS NULL
            """);
        Assert.True(result.Ok, result.Describe());
    }

    [Fact]
    public async Task WrongValue_Fails()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            SELECT-ROWS WHERE Name = "Gadget"
            EXPECT Row TYPEHINT foreground = "grey"
            """);
        Assert.False(result.Ok);
    }

    [Fact]
    public async Task DetailQueryRow_ReadsItsHint()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/ProductCategories
            OPEN-ROW WHERE Name = "Electronics"
            SEARCH Detail "ProductCategory_Products"
            SELECT-ROWS Detail "ProductCategory_Products" WHERE Name = "Gadget"
            EXPECT Detail "ProductCategory_Products" Row TYPEHINT foreground = "red"
            """);
        Assert.True(result.Ok, result.Describe());
    }

    [Theory]
    [InlineData("SELECT-ROWS NONE")]
    [InlineData("SELECT-ROWS ALL")]
    [InlineData("SELECT-ROWS WHERE Category = \"1\"")]
    public async Task NotExactlyOneSelectedRow_Fails(string select)
    {
        var result = await Run($"""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            {select}
            EXPECT Row TYPEHINT foreground IS NULL
            """);
        Assert.False(result.Ok);
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.AssertFailed && d.Message.Contains("exactly one selected row"));
    }

    [Fact]
    public async Task UnknownColumn_FailsToResolve()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            SELECT-ROWS WHERE Name = "Gadget"
            EXPECT Row Column Colour TYPEHINT background = "pink"
            """);
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.ResolveAttribute);
    }
}
