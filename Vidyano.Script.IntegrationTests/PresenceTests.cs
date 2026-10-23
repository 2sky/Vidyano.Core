using Vidyano.Script.Diagnostics;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.IntegrationTests;

/// <summary>
/// <c>EXPECT Attribute X IS [NOT] PRESENT</c> / <c>EXPECT Query.Columns[X] IS [NOT] PRESENT</c> against the REAL
/// in-process server: <see cref="Product.Discontinued"/> is in the model but the server strips it — the attribute
/// in <see cref="ProductActions.OnLoad"/> (<c>RemoveAttribute</c>), the column in
/// <see cref="ProductActions.QueryExecuted"/> (<c>RemoveColumns</c>). <c>EXPECT Detail "X" IS [NOT] PRESENT</c> checks the
/// current PO's detail queries.
/// </summary>
[Collection(VidyanoAppCollection.Name)]
public sealed class PresenceTests
{
    private readonly VidyanoAppFixture _app;

    public PresenceTests(VidyanoAppFixture app)
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
    public async Task RemovedAttributeAndColumn_AreNotPresent()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            EXPECT Query.Columns[Discontinued] IS NOT PRESENT
            EXPECT Query.Columns[Name] IS PRESENT
            OPEN-ROW WHERE Name = "Widget"
            EXPECT Attribute Discontinued IS NOT PRESENT
            EXPECT Attribute Name IS PRESENT
            EXPECT Attribute Secret IS PRESENT
            """));
    }

    [Fact]
    public async Task DetailQueryColumn_IsNotPresent()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/ProductCategories
            OPEN-ROW WHERE Name = "Tools"
            SEARCH Detail "ProductCategory_Products"
            EXPECT Detail "ProductCategory_Products" Query.Columns[Discontinued] IS NOT PRESENT
            EXPECT Detail "ProductCategory_Products" Query.Columns[Name] IS PRESENT
            """));
    }

    [Fact]
    public async Task DetailQuery_IsPresentOnlyWhereTheServerAddedIt()
    {
        // ProductCategory's OnLoad adds ProductCategory_Products; a Product carries no such detail query.
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/ProductCategories
            OPEN-ROW WHERE Name = "Tools"
            EXPECT Detail "ProductCategory_Products" IS PRESENT
            OPEN PersistentObject "Product" "1"
            EXPECT Detail "ProductCategory_Products" IS NOT PRESENT
            """));
    }

    [Fact]
    public async Task IsPresent_OnAMissingDetailQuery_Fails()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN PersistentObject "Product" "1"
            EXPECT Detail "ProductCategory_Products" IS PRESENT
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.AssertFailed);
    }

    [Fact]
    public async Task OtherAssertions_OnAMissingDetailQuery_StillFailToResolve()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN PersistentObject "Product" "1"
            EXPECT Detail "ProductCategory_Products" TotalItems = 0
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.ResolveQuery);
    }

    [Fact]
    public async Task IsPresent_OnARemovedAttribute_Fails()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN PersistentObject "Product" "1"
            EXPECT Attribute Discontinued IS PRESENT
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.AssertFailed);
    }

    [Fact]
    public async Task OtherAssertions_OnARemovedAttribute_StillFailToResolve()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN PersistentObject "Product" "1"
            EXPECT Discontinued IS NULL
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.ResolveAttribute);
    }
}
