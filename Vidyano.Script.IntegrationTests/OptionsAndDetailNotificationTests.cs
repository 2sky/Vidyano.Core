using Vidyano.Script.Diagnostics;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.IntegrationTests;

/// <summary>
/// <c>EXPECT Attribute X OPTIONS [NOT] CONTAINS "…"</c> and <c>EXPECT Detail "…" Notification</c> against the REAL
/// in-process server. <see cref="ProductCategory.Zone"/> is a KeyValueList whose options
/// <see cref="ProductCategoryActions.OnLoad"/> filters to <c>fn=FlandersNorth</c> / <c>wa=Wallonia</c>; the
/// <see cref="ShopContext.WarnedCategoryName"/> category opens with its own warning, and <see cref="FailOnServer"/>
/// fails on its Products detail.
/// </summary>
[Collection(VidyanoAppCollection.Name)]
public sealed class OptionsAndDetailNotificationTests
{
    private readonly VidyanoAppFixture _app;

    public OptionsAndDetailNotificationTests(VidyanoAppFixture app)
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

    private async Task AssertFailsWith(string script, string expectedKind)
    {
        var result = await Run(script);
        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == expectedKind);
    }

    private const string OpenTools = """
        SIGN-IN admin / admin
        OPEN MenuItem Home/ProductCategories
        OPEN-ROW WHERE Name = "Tools"
        """;

    [Fact]
    public async Task Options_MatchWholeOptionsByDisplayValueOrKey()
    {
        AssertOk(await Run(OpenTools + """

            EXPECT Attribute Zone OPTIONS CONTAINS "Wallonia"
            EXPECT Attribute Zone OPTIONS CONTAINS "wa"
            EXPECT Attribute Zone OPTIONS CONTAINS "FlandersNorth"
            EXPECT Attribute Zone OPTIONS NOT CONTAINS "Netherlands"
            EXPECT Attribute Zone OPTIONS NOT CONTAINS "Flanders"
            EXPECT Attribute Zone OPTIONS NOT CONTAINS "wallonia"
            """));
    }

    [Theory]
    [InlineData("EXPECT Attribute Zone OPTIONS CONTAINS \"Netherlands\"")]
    [InlineData("EXPECT Attribute Zone OPTIONS CONTAINS \"Flanders\"")]
    [InlineData("EXPECT Attribute Zone OPTIONS NOT CONTAINS \"Wallonia\"")]
    [InlineData("EXPECT Attribute Zone OPTIONS NOT CONTAINS \"fn\"")]
    public async Task Options_WrongOption_Fails(string expect) =>
        await AssertFailsWith(OpenTools + "\n" + expect, ErrorKind.AssertFailed);

    [Fact]
    public async Task Options_UnknownAttribute_FailsToResolve() =>
        await AssertFailsWith(OpenTools + "\nEXPECT Attribute Zones OPTIONS CONTAINS \"Wallonia\"", ErrorKind.ResolveAttribute);

    [Fact]
    public async Task Options_OnAnAttributeWithoutOptions_Fails() =>
        await AssertFailsWith(OpenTools + "\nEXPECT Attribute Name OPTIONS CONTAINS \"Tools\"", ErrorKind.ParseUnexpectedToken);

    private const string OpenArchive = """
        SIGN-IN admin / admin
        OPEN MenuItem Home/ProductCategories
        OPEN-ROW WHERE Name = "Archive"
        """;

    [Fact]
    public async Task DetailNotification_ReadsTheDetailError_NotTheParentWarning()
    {
        ShopContext.AddCategory(ShopContext.WarnedCategoryName);

        AssertOk(await Run(OpenArchive + $"""

            EXPECT Notification = "{ShopContext.CategoryWarning}"
            ACTION Detail "ProductCategory_Products" FailOnServer EXPECTING ERROR
            EXPECT Notification = "{ShopContext.CategoryWarning}"
            EXPECT Notification.Type = "Warning"
            EXPECT Detail "ProductCategory_Products" Notification CONTAINS "Query action failed on the server."
            EXPECT Detail "ProductCategory_Products" Notification.Type = "Error"
            """));
    }

    [Fact]
    public async Task DetailNotification_WrongMessage_Fails()
    {
        ShopContext.AddCategory(ShopContext.WarnedCategoryName);

        await AssertFailsWith(OpenArchive + $"""

            ACTION Detail "ProductCategory_Products" FailOnServer EXPECTING ERROR
            EXPECT Detail "ProductCategory_Products" Notification CONTAINS "{ShopContext.CategoryWarning}"
            """, ErrorKind.AssertFailed);
    }

    [Fact]
    public async Task DetailNotification_IsNull_WhenTheDetailHasNoError()
    {
        ShopContext.AddCategory(ShopContext.WarnedCategoryName);

        AssertOk(await Run(OpenArchive + """

            SEARCH Detail "ProductCategory_Products"
            EXPECT Detail "ProductCategory_Products" Notification IS NULL
            EXPECT Detail "ProductCategory_Products" Notification.Type IS NULL
            """));
    }

    [Fact]
    public async Task DetailNotification_UnknownDetail_FailsToResolve() =>
        await AssertFailsWith(OpenTools + "\nEXPECT Detail \"ProductCategory_Nope\" Notification IS NULL", ErrorKind.ResolveQuery);
}
