using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.IntegrationTests;

/// <summary>
/// Core's built-in action gating (IsVisible / CanExecute) must match the web client's, so an <c>ACTION</c> or
/// <c>EXPECT Action … IS VISIBLE | AVAILABLE</c> answers what a browser would show. Covers BulkEdit's selection
/// rule (core/query.ts: limited to one row only when the query sets <c>disableBulkEdit</c>) and the
/// Edit / EndEdit / CancelEdit toggles (core/actions.ts). The New + AddReference pair is covered next to the
/// built-in AddReference picker tests in <see cref="VerbFamilyTests"/>.
/// </summary>
[Collection(VidyanoAppCollection.Name)]
public sealed class ActionGatingTests
{
    private readonly VidyanoAppFixture _app;

    public ActionGatingTests(VidyanoAppFixture app)
    {
        _app = app;
        ShopContext.Reset();
    }

    private Task<ScriptResult> Run(string script) =>
        VidyanoScript.RunAsync(script, new VidyanoScriptOptions { Backend = _app.Backend });

    private static void AssertOk(ScriptResult result) => Assert.True(result.Ok, result.Describe());

    [Fact]
    public async Task BulkEdit_QueryWithoutDisableBulkEdit_EditsEveryRow()
    {
        // Documents leaves disableBulkEdit off, so BulkEdit runs on any number of rows (Core used to force "=1"
        // for every query). The changed attribute lands on every selected row.
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Documents
            SELECT-ROWS ALL
            EXPECT Action BulkEdit IS AVAILABLE
            ACTION BulkEdit
            EXPECT IsInEdit = true
            SET Name = "Archived"
            SAVE
            SEARCH ""
            SELECT-ROWS WHERE Name = "Archived"
            EXPECT Selection.Count = 2
            """));
    }

    [Fact]
    public async Task BulkEdit_DisableBulkEdit_LimitsToOneRow()
    {
        // Products sets disableBulkEdit (its type has a PersistentObjectActions class), so BulkEdit needs exactly
        // one selected row there.
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            SELECT-ROWS WHERE Category = "Tools"
            EXPECT Selection.Count = 2
            EXPECT Action BulkEdit IS NOT AVAILABLE
            SELECT-ROWS WHERE Name = "Widget"
            EXPECT Action BulkEdit IS AVAILABLE
            """));
    }

    [Fact]
    public async Task EditActions_ToggleWithEditMode()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Widget"
            EXPECT Action Edit IS VISIBLE
            EXPECT Action EndEdit IS NOT VISIBLE
            EXPECT Action CancelEdit IS NOT VISIBLE
            EDIT
            EXPECT Action Edit IS NOT VISIBLE
            EXPECT Action EndEdit IS VISIBLE
            EXPECT Action EndEdit IS NOT AVAILABLE
            EXPECT Action CancelEdit IS VISIBLE
            EXPECT Action CancelEdit IS AVAILABLE
            SET Color = "Purple"
            EXPECT Action EndEdit IS AVAILABLE
            """));
    }

    [Fact]
    public async Task CancelEdit_StayInEdit_AvailableOnlyOnceDirty()
    {
        // "Electronics" opens StayInEdit (ProductCategoryActions.OnLoad): always in edit, so there is nothing to
        // cancel until something changed.
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/ProductCategories
            OPEN-ROW WHERE Name = "Electronics"
            EXPECT IsInEdit = true
            EXPECT Action CancelEdit IS VISIBLE
            EXPECT Action CancelEdit IS NOT AVAILABLE
            SET Name = "Gadgets"
            EXPECT Action CancelEdit IS AVAILABLE
            """));
    }
}
