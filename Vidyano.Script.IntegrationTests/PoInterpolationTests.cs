using Vidyano.Script.Diagnostics;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.IntegrationTests;

/// <summary>
/// <c>{{PO.ObjectId}}</c> / <c>{{PO.Attr.&lt;name&gt;}}</c> against the REAL in-process server: a script keeps a value
/// it saw on the current record (including the id of a record it created) and reuses it in later steps.
/// </summary>
[Collection(VidyanoAppCollection.Name)]
public sealed class PoInterpolationTests
{
    private readonly VidyanoAppFixture _app;

    public PoInterpolationTests(VidyanoAppFixture app)
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
    public async Task CapturedValues_AreReusedLater()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Gadget"
            @id = {{PO.ObjectId}}
            @name = {{PO.Attr.Name}}
            @type = {{PO.Type}}
            EXPECT {{id}} = "2"
            GO-BACK
            OPEN PersistentObject "{{type}}" "{{id}}"
            EXPECT Name = "{{name}}"
            EXPECT Color = "Red"
            """));
    }

    [Fact]
    public async Task IdOfARecordCreatedAtRunTime_IsCaptured()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            ACTION New
            SET Name = "Doohickey"
            SAVE
            OPEN-ROW WHERE Name = "Doohickey"
            @newId = {{PO.ObjectId}}
            EXPECT {{newId}} IS NOT NULL
            GO-BACK
            OPEN PersistentObject "Product" "{{newId}}"
            EXPECT Name = "Doohickey"
            """));
    }

    [Fact]
    public async Task HiddenAttribute_IsGuardedLikeExpect()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN PersistentObject "Product" "1"
            @secret = {{PO.Attr.Secret}}
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.GuardAttributeHidden);
    }

    [Fact]
    public async Task UnknownAttribute_FailsToResolve()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN PersistentObject "Product" "1"
            @x = {{PO.Attr.Nmae}}
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.ResolveAttribute && d.Hint != null && d.Hint.Contains("Name"));
    }

    [Fact]
    public async Task NoCurrentPo_Fails()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            @id = {{PO.ObjectId}}
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.StateNoCurrentPo);
    }
}
