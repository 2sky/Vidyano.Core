using Vidyano.Script.Diagnostics;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.IntegrationTests;

/// <summary>
/// <c>FOLLOW-NAVIGATE</c> against the REAL in-process Vidyano server: <see cref="NavigateTo"/> queues a
/// <c>Navigate(path)</c> client operation, and the verb resolves the path through the Application's real
/// <c>Routes</c> attribute (built by the server's <c>BuildRoutes</c>) — the same table the web client routes with.
/// </summary>
[Collection(VidyanoAppCollection.Name)]
public sealed class FollowNavigateTests
{
    private readonly VidyanoAppFixture _app;

    public FollowNavigateTests(VidyanoAppFixture app)
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
    public async Task KebabRoute_WithObjectId_OpensThePersistentObject()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Widget"
            ACTION NavigateTo (Path="product-category/2")
            EXPECT ClientOperation Navigate = "product-category/2"
            FOLLOW-NAVIGATE AS @category
            EXPECT NavStack.Depth = 3
            EXPECT PO.Type = "ProductCategory"
            EXPECT PO.ObjectId = "2"
            EXPECT Name = "Electronics"
            """));
    }

    [Fact]
    public async Task RawRouteName_AndProgramUnitPrefix_Resolve()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN PersistentObject "Product" "1"
            ACTION NavigateTo (Path="Home/ProductCategory/1")
            FOLLOW-NAVIGATE
            EXPECT PO.Type = "ProductCategory"
            EXPECT Name = "Tools"
            """));
    }

    [Fact]
    public async Task RawPersistentObjectRoute_Resolves()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN PersistentObject "Product" "1"
            ACTION NavigateTo (Path="persistent-object.Product/3")
            FOLLOW-NAVIGATE
            EXPECT PO.Type = "Product"
            EXPECT Name = "Gizmo"
            """));
    }

    [Fact]
    public async Task QueryRoute_OpensTheQuery()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN PersistentObject "Product" "1"
            ACTION NavigateTo (Path="product-categories")
            FOLLOW-NAVIGATE
            EXPECT NavStack.Top.Kind = "Query"
            EXPECT NavStack.Top.Name = "ProductCategories"
            """));
    }

    [Fact]
    public async Task NoNavigate_FailsWithStateNoNavigate()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN PersistentObject "Product" "1"
            FOLLOW-NAVIGATE
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.StateNoNavigate);
    }

    [Fact]
    public async Task NavigateFromAnOlderVerb_IsNotFollowed()
    {
        // Only the immediately preceding verb's operations count (EXPECTs don't reset them; other verbs do).
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN PersistentObject "Product" "1"
            ACTION NavigateTo (Path="product-category/1")
            EDIT
            CANCEL
            FOLLOW-NAVIGATE
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.StateNoNavigate);
    }

    [Fact]
    public async Task TwoNavigates_AreAmbiguous()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN PersistentObject "Product" "1"
            ACTION NavigateTo (Path="product-category/1", Twice="true")
            FOLLOW-NAVIGATE
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.ResolveNavigate);
    }

    [Fact]
    public async Task UnknownRoute_FailsWithResolveNavigate()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN PersistentObject "Product" "1"
            ACTION NavigateTo (Path="no-such-page/1")
            FOLLOW-NAVIGATE
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.ResolveNavigate);
    }
}
