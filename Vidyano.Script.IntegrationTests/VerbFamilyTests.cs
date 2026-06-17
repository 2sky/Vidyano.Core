using Vidyano.Script.Diagnostics;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.IntegrationTests;

/// <summary>
/// One .visc per verb family, each run against the REAL in-process Vidyano server — no
/// demo.vidyano.com, no database, no mocking. These are the regression tests the .visc engine could
/// never have in CI before (the old samples needed a live RavenDB at :44353). Data is re-seeded per
/// test (see the constructor); each script signs in fresh.
/// </summary>
[Collection(VidyanoAppCollection.Name)]
public sealed class VerbFamilyTests
{
    private readonly VidyanoAppFixture _app;

    public VerbFamilyTests(VidyanoAppFixture app)
    {
        _app = app;
        ShopContext.Reset();
    }

    private Task<ScriptResult> Run(string script, VidyanoScriptOptions? options = null)
    {
        options ??= new VidyanoScriptOptions();
        options.Backend = _app.Backend;
        return VidyanoScript.RunAsync(script, options);
    }

    private static void AssertOk(ScriptResult result) => Assert.True(result.Ok, result.Describe());

    /// <summary>Every diagnostic across every statement of the run — failures, skips, and warnings that
    /// rode on passing statements alike. Lets a test assert on a specific <see cref="ErrorKind"/> without
    /// re-walking the step/statement tree each time.</summary>
    private static IEnumerable<Diagnostic> AllDiagnostics(ScriptResult result) =>
        result.Steps.SelectMany(s => s.Statements).SelectMany(s => s.Diagnostics);

    [Fact]
    public async Task SignIn_Open_QueryCount()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            EXPECT NavStack.Depth = 1
            EXPECT NavStack.Top.Kind = "Query"
            EXPECT NavStack.Top.Name = "Products"
            EXPECT TotalItems = 3
            """));
    }

    [Fact]
    public async Task OpenRow_GoBack_NavStack()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Widget"
            EXPECT NavStack.Depth = 2
            EXPECT NavStack.Top.Kind = "PersistentObject"
            EXPECT NavStack.Top.Name = "Product"
            GO-BACK
            EXPECT NavStack.Depth = 1
            EXPECT NavStack.Top.Kind = "Query"
            """));
    }

    [Fact]
    public async Task Edit_Set_Save_Persists()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Widget"
            EDIT
            EXPECT IsInEdit = true
            SET Color = "Purple"
            EXPECT Color = "Purple"
            SAVE
            EXPECT NavStack.Depth = 1
            SEARCH ""
            OPEN-ROW WHERE Name = "Widget"
            EXPECT Color = "Purple"
            """));
    }

    [Fact]
    public async Task Action_ReturnsNotification()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Widget"
            ACTION HelloWorld
            EXPECT Notification.Type = "OK"
            EXPECT Notification MATCHES "Hello"
            """));
    }

    [Fact]
    public async Task Save_ExpectingError_OnSentinel()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Gadget"
            EDIT
            SET Color = "Forbidden"
            SAVE EXPECTING ERROR
            EXPECT Notification.Type = "Error"
            EXPECT Notification MATCHES "not allowed"
            """));
    }

    [Fact]
    public async Task TriggersRefresh_OnRefresh_UpdatesOtherAttribute()
    {
        // Setting Product.Trigger (marked TriggersRefresh) round-trips through ProductActions.OnRefresh,
        // which mirrors it into the unrelated Product.Echo. Regression for the client refresh path:
        // SET -> SetValueAsync -> RefreshAttributesAsync, with the server-modified Echo flowing back so
        // EXPECT sees a value the SET itself never wrote.
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Widget"
            EDIT
            SET Trigger = "Purple"
            EXPECT Echo = "echo:Purple"
            """));
    }

    [Fact]
    public async Task Follow_Reference_OpensTarget()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Widget"
            FOLLOW Category
            EXPECT NavStack.Top.Kind = "PersistentObject"
            EXPECT NavStack.Top.Name = "ProductCategory"
            """));
    }

    [Fact]
    public async Task DetailQuery_LoadsChildRows()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/ProductCategories
            OPEN-ROW WHERE Name = "Tools"
            SEARCH Detail "ProductCategory_Products"
            EXPECT Detail "ProductCategory_Products" TotalItems = 2
            """));
    }

    [Fact]
    public async Task SetFile_AttachesToBinaryFileAttribute()
    {
        var dir = Path.Combine(Path.GetTempPath(), "visc-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "payload.txt"), "hello from a file");
        try
        {
            AssertOk(await Run("""
                SIGN-IN admin / admin
                OPEN MenuItem Home/Documents
                OPEN-ROW WHERE Name = "Spec"
                EDIT
                SET File = FILE "payload.txt"
                SAVE
                """, new VidyanoScriptOptions { FileRoot = dir }));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task TranslatedString_SetAndExpect_PerLanguage_RoundTrips()
    {
        // Product.Title is a TranslatedString ({"en":…,"nl":…,"de":…}). Pin the session language so the
        // bare SET/EXPECT (current-language) path is deterministic, set the current language and ONE other
        // (nl), leave de untouched, save, reopen, and assert: the current-language value persisted, the
        // explicit nl write persisted, and the untouched de keeps its seed (the client merge preserves it).
        AssertOk(await Run("""
            SIGN-IN admin / admin LANGUAGE en
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Widget"
            EXPECT Title = "Widget"                  ## current-language (en) seed via Value
            EXPECT Title LANGUAGE nl = "Hulpmiddel"  ## seeded Dutch translation
            EDIT
            SET Title = "Widget-en"                  ## current language (en)
            SET Title LANGUAGE nl = "Hulpmiddel-2"   ## a specific language
            EXPECT Title LANGUAGE nl = "Hulpmiddel-2"
            SAVE
            SEARCH ""
            OPEN-ROW WHERE Name = "Widget"
            EXPECT Title = "Widget-en"               ## current language persisted
            EXPECT Title LANGUAGE en = "Widget-en"   ## same, addressed explicitly
            EXPECT Title LANGUAGE nl = "Hulpmiddel-2"
            EXPECT Title LANGUAGE de = "Werkzeug"    ## untouched language preserved through the merge
            """));
    }

    [Fact]
    public async Task TranslatedString_Language_OnNonTranslatedAttribute_Fails()
    {
        // LANGUAGE only applies to a TranslatedString — Color is a plain String, so the SET fails loudly
        // rather than silently dropping the clause.
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Widget"
            EDIT
            SET Color LANGUAGE nl = "Blauw"
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.ParseUnexpectedToken && d.Message.Contains("LANGUAGE"));
    }

    [Fact]
    public async Task Confirm_AnswersRetryDialog()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Widget"
            ACTION AskFirst
            EXPECT NavStack.Top.Kind = "RetryDialog"
            EXPECT RetryDialog.Title = "Are you sure?"
            CONFIRM "Yes"
            EXPECT Notification.Type = "OK"
            EXPECT Notification MATCHES "Yes"
            """));
    }

    // --- hidden-attribute SET across guard modes -------------------------------------------------
    //
    // Product.Secret is hidden (ProductActions.OnLoad sets Visibility = Never), modelling a field only a
    // custom web component edits. Core itself never blocks setting a hidden-but-editable attribute
    // (PersistentObjectAttribute.UpdateValue gates on read-only alone), so these pin the .visc SET guard:
    // it tiers with reachability — navigation rejects, audit warns-but-allows, direct allows silently.

    [Fact]
    public async Task HiddenAttribute_NavigationMode_RejectsSet()
    {
        // Default mode. The standard UI would never surface Secret, so SET is rejected before it touches
        // the value — the regression that would have let this through is exactly the one the users hit.
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Widget"
            EDIT
            SET Secret = "classified"
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.GuardAttributeHidden);
    }

    [Fact]
    public async Task HiddenAttribute_DirectMode_AllowsAndRoundTrips()
    {
        // @mode = direct is the documented escape hatch for the custom-component path: SET reaches the
        // hidden attribute, SAVE posts it, and reopening reads it back (the EXPECT read tiers the same way).
        AssertOk(await Run("""
            @mode = direct
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Widget"
            EDIT
            SET Secret = "classified"
            SAVE
            SEARCH ""
            OPEN-ROW WHERE Name = "Widget"
            EXPECT Secret = "classified"
            """));
    }

    [Fact]
    public async Task HiddenAttribute_AuditMode_WarnsButAllows()
    {
        // Audit tier: the set is allowed (so the round-trip persists like direct) but carries a non-failing
        // warning so a reviewer sees the custom-component path was exercised.
        var result = await Run("""
            @mode = audit
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Widget"
            EDIT
            SET Secret = "classified"
            SAVE
            SEARCH ""
            OPEN-ROW WHERE Name = "Widget"
            EXPECT Secret = "classified"
            """);

        AssertOk(result);
        // The warning rides on the (passing) SET — same kind as the navigation failure, but the run is ok.
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.GuardAttributeHidden);
    }

    [Fact]
    public async Task ReadOnlyAttribute_DirectMode_StillRejected()
    {
        // Read-only is the hard guard the mode tier never relaxes: a read-only attribute is genuinely not
        // settable (even by a custom component), so direct — the most permissive mode — still rejects it.
        // This pins that the hidden-attribute escape hatch did not also widen the read-only guard.
        var result = await Run("""
            @mode = direct
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Widget"
            EDIT
            SET Locked = "tampered"
            """);

        Assert.False(result.Ok, result.Describe());
        Assert.Contains(AllDiagnostics(result), d => d.Kind == ErrorKind.GuardAttributeReadOnly);
    }

    [Fact]
    public async Task NamedSessions_AreIsolated()
    {
        // Two named identities over the SAME in-process server, each with its own cookie jar (minted via
        // the adapter's MintIsolatedAsync). Proves named sessions reach the in-memory TestServer at all —
        // the old mintFresh hard-built a socket client a TestServer can't answer — and that their nav
        // state is independent: @b is freshly signed in (empty stack) while @a keeps its open Query.
        AssertOk(await Run("""
            SIGN-IN @a = admin / admin
            OPEN MenuItem Home/Products
            EXPECT NavStack.Depth = 1
            EXPECT NavStack.Top.Name = "Products"
            SIGN-IN @b = admin / admin
            EXPECT NavStack.Depth = 0
            OPEN MenuItem Home/ProductCategories
            EXPECT NavStack.Top.Name = "ProductCategories"
            USE @a
            EXPECT NavStack.Depth = 1
            EXPECT NavStack.Top.Name = "Products"
            """));
    }
}
