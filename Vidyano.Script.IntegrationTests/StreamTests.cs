using Vidyano.Script.Diagnostics;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.IntegrationTests;

/// <summary>
/// The stream auto-fetch and the <c>EXPECT Stream.*</c> subjects, run against the REAL in-process Vidyano
/// server. <see cref="DownloadSpec"/> returns a <c>Vidyano.RegisteredStream</c> and
/// <see cref="ProductActions.OnGetStream"/> serves the bytes on the follow-up fetch — so these tests exercise
/// the same two-step download flow the web client performs automatically, not a mock.
/// </summary>
[Collection(VidyanoAppCollection.Name)]
public sealed class StreamTests
{
    private readonly VidyanoAppFixture _app;

    public StreamTests(VidyanoAppFixture app)
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

    [Fact]
    public async Task Stream_ActionAutoFetches_AndSubjectsRead()
    {
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Widget"
            ACTION DownloadSpec
            EXPECT Stream IS NOT NULL
            EXPECT Stream.Name = "spec.txt"
            EXPECT Stream.Length > 0
            EXPECT Stream.Text CONTAINS "%PDF"
            EXPECT Stream.Text CONTAINS "Widget"
            """));
    }

    [Fact]
    public async Task Stream_IsClearedByTheNextVerb()
    {
        // The capture is per-verb, like the ClientOperations buffer: after the download action EXPECT reads
        // it, but once another executable verb runs the stale stream is gone.
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Widget"
            EXPECT Stream IS NULL
            ACTION DownloadSpec
            EXPECT Stream IS NOT NULL
            GO-BACK
            EXPECT Stream IS NULL
            """));
    }

    [Fact]
    public async Task Stream_ServerFault_ArrivesAsBodyText()
    {
        // A server-side OnGetStream fault is served as the GetStream *response body* (a 2xx), not a transport
        // error — so the ACTION succeeds and the fault text is the delivered stream, assertable via Stream.Text.
        // (The runner's fetch-error catch, which lands a notification and fails the verb, is for a genuine
        // non-2xx GetStream — the harder-to-provoke transport path.)
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Widget"
            ACTION DownloadMissing
            EXPECT Stream IS NOT NULL
            EXPECT Stream.Text CONTAINS "Blob 'missing' could not be found."
            """));
    }

    [Theory]
    [InlineData("ACTION DownloadSpec")]
    [InlineData("ACTION DownloadSpec = \"Text\"")]
    public async Task Stream_IsFetchedExactlyOnce(string action)
    {
        // The option form runs through Core's ActionBase.Execute, which fetches the stream itself and delivers
        // it via Hooks.OnStream; the runner must buffer that delivery rather than fetch the stream again.
        AssertOk(await Run($"""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Widget"
            {action}
            EXPECT Stream.Name = "spec.txt"
            EXPECT Stream.Text CONTAINS "Widget"
            """));
        Assert.Equal(1, DownloadSpec.Fetches);
    }

    [Fact]
    public async Task Stream_DownloadActionDoesNotPushFrame()
    {
        // A returned RegisteredStream is fetched, not navigated to — the PO the action ran on stays on top.
        AssertOk(await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            OPEN-ROW WHERE Name = "Widget"
            EXPECT NavStack.Depth = 2
            ACTION DownloadSpec
            EXPECT NavStack.Depth = 2
            EXPECT NavStack.Top.Kind = "PersistentObject"
            EXPECT NavStack.Top.Name = "Product"
            """));
    }
}
