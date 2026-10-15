using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.IntegrationTests;

/// <summary>
/// Query state after an action refreshes the way the web client's does, so "act, then EXPECT the grid" needs no
/// SEARCH: an action's <c>RefreshQueryOnCompleted</c> (and <c>KeepSelectionOnRefresh</c>), the server's <c>Refresh</c>
/// client operations, and a closed Add-Reference picker. Each case also pins where the browser would NOT refresh, so
/// the script never sees fresher data than a user would. Seed: 4 products, categories Tools (Widget, Gizmo) and
/// Electronics (Gadget).
/// </summary>
[Collection(VidyanoAppCollection.Name)]
public sealed class RefreshTests
{
    private readonly VidyanoAppFixture _app;

    public RefreshTests(VidyanoAppFixture app)
    {
        _app = app;
        ShopContext.Reset();
    }

    private Task<ScriptResult> Run(string script) =>
        VidyanoScript.RunAsync(script, new VidyanoScriptOptions { Backend = _app.Backend });

    private static void AssertOk(ScriptResult result) => Assert.True(result.Ok, result.Describe());

    // --- RefreshQueryOnCompleted / KeepSelectionOnRefresh -----------------------------------------

    [Fact]
    public async Task Action_RefreshQueryOnCompleted_ReSearchesTheGrid()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            EXPECT TotalItems = 4
            ACTION AddSample
            EXPECT TotalItems = 5
            """));
    }

    [Fact]
    public async Task Action_WithoutRefreshQueryOnCompleted_LeavesTheGridStale_UntilSearch()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            ACTION AddSampleNoRefresh
            EXPECT TotalItems = 4
            SEARCH ""
            EXPECT TotalItems = 5
            """));
    }

    [Fact]
    public async Task Action_Refresh_ClearsTheSelection()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            SELECT-ROWS WHERE Name = "Widget"
            ACTION AddSample
            EXPECT TotalItems = 5
            EXPECT Selection.Count = 0
            """));
    }

    [Fact]
    public async Task Action_KeepSelectionOnRefresh_KeepsTheSelectedRows()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            SELECT-ROWS WHERE Name = "Widget"
            ACTION AddSampleKeepSelection
            EXPECT TotalItems = 5
            EXPECT Selection.Count = 1
            """));
    }

    [Fact]
    public async Task Action_KeepSelectionOnRefresh_KeepsAnInverseSelectAll()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            SELECT-ROWS ALL EXCEPT WHERE Name = "Widget"
            ACTION AddSampleKeepSelection
            EXPECT TotalItems = 5
            EXPECT Selection.AllSelected = true
            EXPECT Selection.Count = 1
            """));
    }

    [Fact]
    public async Task OptionAction_RefreshKeepsTheSelection()
    {
        // `ACTION X = "label"` runs through Core's ActionBase.Execute(option), not the direct call: it must refresh by
        // the same rules (KeepSelectionOnRefresh).
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            SELECT-ROWS WHERE Name = "Widget"
            ACTION AddSampleWithOptions = "Quietly"
            EXPECT TotalItems = 5
            EXPECT Selection.Count = 1
            """));
    }

    [Theory]
    [InlineData("""= "Announce" """)]          // Core's Execute(option)
    [InlineData("""(MenuLabel="Announce")""")] // the direct call
    public async Task Action_ReturningANotification_StillReSearchesTheGrid(string form)
    {
        // The web client shows a returned notification once the re-search is done instead of skipping the re-search.
        AssertOk(await Run($$"""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            SELECT-ROWS WHERE Name = "Widget"
            ACTION AddSampleWithOptions {{form}}
            EXPECT TotalItems = 5
            EXPECT Selection.Count = 1
            EXPECT Notification = "{{AddSampleWithOptions.Announcement}}"
            EXPECT Notification.Type = "OK"
            """));
    }

    // --- Query result notifications ------------------------------------------------------------------
    // Like the web client (query.ts #setResult), every query result sets the query's notification: a server
    // notification (QueryExecuted → args.Result.AddNotification) is shown, and a result without one clears it.

    [Fact]
    public async Task Search_ShowsTheResultNotification_AndTheNextSearchClearsIt()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            EXPECT Notification IS NULL
            ACTION AddSampleNoRefresh (QueryNotification="Heads up")
            SEARCH ""
            EXPECT Notification = "Heads up"
            EXPECT Notification.Type = "Notice"
            SEARCH ""
            EXPECT Notification IS NULL
            """));
    }

    [Fact]
    public async Task OpenQuery_ShowsTheNotificationOfItsFirstResult()
    {
        ShopContext.PendingQueryNotification = "Welcome";
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            EXPECT Notification = "Welcome"
            SEARCH ""
            EXPECT Notification IS NULL
            """));
    }

    [Fact]
    public async Task Action_Refresh_ShowsTheReSearchsNotification()
    {
        // The show-once notification is consumed by the post-action re-search, so only that result can surface it.
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            ACTION AddSample (QueryNotification="Sample is pending review")
            EXPECT TotalItems = 5
            EXPECT Notification = "Sample is pending review"
            """));
    }

    [Fact]
    public async Task Action_ReturnedNotification_YieldsToTheReSearchsNotification()
    {
        // action.ts _onExecute shows a returned notification after the re-search only when the search left none.
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            ACTION AddSampleWithOptions (MenuLabel="Announce", QueryNotification="From the search")
            EXPECT TotalItems = 5
            EXPECT Notification = "From the search"
            """));
    }

    [Fact]
    public async Task Action_OnADetailQuery_ReSearchesTheDetail()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/ProductCategories
            OPEN-ROW WHERE Name = "Tools"
            SEARCH Detail "ProductCategory_Products"
            EXPECT Detail "ProductCategory_Products" TotalItems = 2
            ACTION Detail "ProductCategory_Products" AddSample
            EXPECT Detail "ProductCategory_Products" TotalItems = 3
            """));
    }

    [Fact]
    public async Task Delete_ReSearchesTheGrid()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            SELECT-ROWS WHERE Name = "Gizmo"
            ACTION Delete
            EXPECT TotalItems = 3
            """));
    }

    // --- Refresh client operations -----------------------------------------------------------------

    [Fact]
    public async Task RefreshOperation_ForAQuery_ReSearchesASearchedDetail()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/ProductCategories
            OPEN-ROW WHERE Name = "Tools"
            SEARCH Detail "ProductCategory_Products"
            EXPECT Detail "ProductCategory_Products" TotalItems = 2
            ACTION Reshuffle (Refresh="Detail")
            EXPECT ClientOperation Refresh
            EXPECT Detail "ProductCategory_Products" TotalItems = 3
            """));
    }

    [Fact]
    public async Task RefreshOperation_ForAQuery_LeavesANeverLoadedDetailUnloaded()
    {
        // The browser only re-searches a query it has searched; a detail nobody opened stays lazy.
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/ProductCategories
            OPEN-ROW WHERE Name = "Tools"
            ACTION Reshuffle (Refresh="Detail")
            EXPECT Detail "ProductCategory_Products" TotalItems = 0
            SEARCH Detail "ProductCategory_Products"
            EXPECT Detail "ProductCategory_Products" TotalItems = 3
            """));
    }

    [Fact]
    public async Task RefreshOperation_ForAQuery_ReSearchesTheQueryBeneathThePo()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/ProductCategories
            EXPECT TotalItems = 2
            OPEN-ROW WHERE Name = "Tools"
            ACTION Reshuffle (Refresh="Categories")
            GO-BACK
            EXPECT TotalItems = 3
            """));
    }

    [Fact]
    public async Task WithoutRefreshOperation_TheQueryBeneathThePoStaysStale()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/ProductCategories
            OPEN-ROW WHERE Name = "Tools"
            SEARCH Detail "ProductCategory_Products"
            ACTION Reshuffle
            EXPECT Detail "ProductCategory_Products" TotalItems = 2
            EXPECT Name = "Tools"
            GO-BACK
            EXPECT TotalItems = 2
            """));
    }

    [Fact]
    public async Task RefreshOperation_ForAPersistentObject_RefetchesTheOpenPo()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/ProductCategories
            OPEN-ROW WHERE Name = "Tools"
            ACTION Reshuffle (Refresh="Self")
            EXPECT Name = "Tools (reshuffled)"
            """));
    }

    // --- Add-Reference pickers -----------------------------------------------------------------------

    [Fact]
    public async Task CustomAddReference_FromAQuery_ConfirmReSearchesThatQuery()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/ProductCategories
            OPEN-ROW WHERE Name = "Tools"
            SEARCH Detail "ProductCategory_Products"
            EXPECT Detail "ProductCategory_Products" TotalItems = 2
            ACTION Detail "ProductCategory_Products" LinkIntoQuery
            ADD-REFERENCE WHERE Name = "Gadget"
            EXPECT Detail "ProductCategory_Products" TotalItems = 3
            """));
    }

    [Fact]
    public async Task CustomAddReference_FromAPo_RefreshesNothing()
    {
        // LinkProducts is a PersistentObject action: it has no query, so the browser re-searches nothing after the
        // add — the already-loaded detail stays stale until it is searched again.
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/ProductCategories
            OPEN-ROW WHERE Name = "Tools"
            SEARCH Detail "ProductCategory_Products"
            ACTION LinkProducts
            ADD-REFERENCE WHERE Name = "Gadget"
            EXPECT Detail "ProductCategory_Products" TotalItems = 2
            SEARCH Detail "ProductCategory_Products"
            EXPECT Detail "ProductCategory_Products" TotalItems = 3
            """));
    }

    [Fact]
    public async Task BuiltInAddReference_Confirm_ReSearchesTheDetail()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/ProductCategories
            OPEN-ROW WHERE Name = "Tools"
            SEARCH Detail "ProductCategory_Members"
            EXPECT Detail "ProductCategory_Members" TotalItems = 2
            ACTION Detail "ProductCategory_Members" AddReference
            ADD-REFERENCE WHERE Name = "Gadget"
            EXPECT Detail "ProductCategory_Members" TotalItems = 3
            """));
    }

    [Fact]
    public async Task BuiltInAddReference_Dismiss_StillReSearchesTheDetail()
    {
        // The browser refreshes once the picker closes either way; a dismiss changes no data, so the re-search shows
        // as the cleared selection.
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/ProductCategories
            OPEN-ROW WHERE Name = "Tools"
            SEARCH Detail "ProductCategory_Members"
            SELECT-ROWS Detail "ProductCategory_Members" WHERE Name = "Widget"
            EXPECT Detail "ProductCategory_Members" Selection.Count = 1
            ACTION Detail "ProductCategory_Members" AddReference
            GO-BACK
            EXPECT Detail "ProductCategory_Members" Selection.Count = 0
            """));
    }
}
