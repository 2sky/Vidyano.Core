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
