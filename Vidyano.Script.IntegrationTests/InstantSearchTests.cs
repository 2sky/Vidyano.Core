using Vidyano.Script.Diagnostics;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.IntegrationTests;

/// <summary>
/// <c>INSTANT-SEARCH</c> / <c>EXPECT InstantSearch.*</c> / <c>OPEN-ROW InstantSearch …</c> against the REAL in-process
/// server: <see cref="ShopAdvanced"/> turns instant search on for <c>admin</c> (off for <c>viewer</c>) and returns the
/// Products whose Name contains the text.
/// </summary>
[Collection(VidyanoAppCollection.Name)]
public sealed class InstantSearchTests
{
    private readonly VidyanoAppFixture _app;

    public InstantSearchTests(VidyanoAppFixture app)
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
    public async Task Search_ReturnsMatchingRows()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            EXPECT InstantSearch.Enabled = true
            EXPECT InstantSearch.Count IS NULL
            INSTANT-SEARCH "g"
            EXPECT InstantSearch.Count = 3
            EXPECT InstantSearch[0].Breadcrumb = "Gadget"
            EXPECT InstantSearch[0].ObjectId = "2"
            EXPECT InstantSearch[0].Label = "Product"
            EXPECT InstantSearch[1].Breadcrumb = "Gizmo"
            EXPECT InstantSearch[2].Breadcrumb = "Widget"
            EXPECT InstantSearch[0].Id MATCHES "^[0-9a-f-]{36}$"
            EXPECT NavStack.Depth = 0
            """));
    }

    [Fact]
    public async Task Search_InterpolatedText()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            @term = "giz"
            INSTANT-SEARCH "{{term}}"
            EXPECT InstantSearch.Count = 1
            EXPECT InstantSearch[0].ObjectId = "3"
            """));
    }

    [Fact]
    public async Task Search_UnknownTerm_ReturnsZeroRows()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            INSTANT-SEARCH "nothing-matches-this"
            EXPECT InstantSearch.Count = 0
            """));
    }

    [Fact]
    public async Task WrongLabel_Fails()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            INSTANT-SEARCH "widget"
            EXPECT InstantSearch[0].Breadcrumb = "Gadget"
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.AssertFailed);
    }

    [Fact]
    public async Task OpenRow_ByIndex_OpensThePersistentObject()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            INSTANT-SEARCH "gizmo"
            OPEN-ROW InstantSearch 0
            EXPECT NavStack.Top.Kind = "PersistentObject"
            EXPECT Name = "Gizmo"
            """));
    }

    [Fact]
    public async Task OpenRow_Where_OpensThePersistentObject()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            INSTANT-SEARCH "g"
            OPEN-ROW InstantSearch WHERE ObjectId = "1"
            EXPECT NavStack.Top.Kind = "PersistentObject"
            EXPECT Name = "Widget"
            """));
    }

    [Fact]
    public async Task OpenRow_Where_NoMatch_Fails()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            INSTANT-SEARCH "g"
            OPEN-ROW InstantSearch WHERE Breadcrumb = "Nope"
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.AssertFailed);
    }

    [Fact]
    public async Task OpenRow_RefusedLoad_ExpectingError()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            INSTANT-SEARCH "faulty"
            OPEN-ROW InstantSearch 0 EXPECTING ERROR
            EXPECT NavStack.Depth = 0
            """));
    }

    [Fact]
    public async Task OpenRow_WithoutSearch_Fails()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN-ROW InstantSearch 0
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.StateNoInstantSearch);
    }

    [Fact]
    public async Task Results_AreClearedByTheNextVerb()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            INSTANT-SEARCH "g"
            EXPECT InstantSearch.Count = 3
            OPEN MenuItem Home/Products
            EXPECT InstantSearch.Count IS NULL
            OPEN-ROW InstantSearch 0
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.StateNoInstantSearch);
        Assert.DoesNotContain(AllDiagnostics(result), d => d.Kind == ErrorKind.AssertFailed);
    }

    [Fact]
    public async Task FeatureOff_EnabledIsFalse()
    {
        AssertOk(await Run("""
            SIGN-IN viewer / viewer
            EXPECT InstantSearch.Enabled = false
            """));
    }

    [Fact]
    public async Task FeatureOff_SearchFails()
    {
        var result = await Run("""
            SIGN-IN viewer / viewer
            INSTANT-SEARCH "g"
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.StateInstantSearchOff);
    }

    [Fact]
    public async Task Requires_GatesOnEnabled()
    {
        AssertOk(await Run("""
            SIGN-IN viewer / viewer
            REQUIRES InstantSearch.Enabled = true
            INSTANT-SEARCH "g"
            """));
    }
}
