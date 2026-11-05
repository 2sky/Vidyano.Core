using Vidyano.Script.Diagnostics;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.IntegrationTests;

/// <summary>
/// <c>ACTION</c> on a streaming action (<c>AsyncStreamingAction</c>) against the REAL in-process server: <see cref="StreamProgress"/>
/// answers <c>ExecuteAction</c> with Server-Sent Events — progress messages, a Refresh client operation and a dialog notification —
/// read through <c>EXPECT StreamingAction.*</c> and <c>EXPECT ClientOperation</c>.
/// </summary>
[Collection(VidyanoAppCollection.Name)]
public sealed class StreamingActionTests
{
    private readonly VidyanoAppFixture _app;

    public StreamingActionTests(VidyanoAppFixture app)
    {
        _app = app;
        ShopContext.Reset();
    }

    private Task<ScriptResult> Run(string script) =>
        VidyanoScript.RunAsync(script, new VidyanoScriptOptions { Backend = _app.Backend });

    private static IEnumerable<Diagnostic> AllDiagnostics(ScriptResult result) =>
        result.Steps.SelectMany(s => s.Statements).SelectMany(s => s.Diagnostics);

    [Fact]
    public async Task StreamingAction_RunsToTheEnd_AndExposesWhatItSent()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            SELECT-ROWS WHERE Name = "Widget"
            ACTION StreamProgress
            EXPECT StreamingAction.Messages = "Processing Widget"
            EXPECT StreamingAction.Notification = "Processed 1 products."
            EXPECT StreamingAction.Notification.Type = "Notice"
            EXPECT ClientOperation Refresh
            EXPECT NavStack.Top.Kind = "Query"
            """);
        Assert.True(result.Ok, result.Describe());
    }

    [Fact]
    public async Task Messages_ArriveInOrder_OnePerLine()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            SELECT-ROWS WHERE Color = "Blue"
            ACTION StreamProgress
            EXPECT StreamingAction.Messages MATCHES "^Processing [A-Za-z]+\nProcessing [A-Za-z]+$"
            EXPECT StreamingAction.Notification = "Processed 2 products."
            """);
        Assert.True(result.Ok, result.Describe());
    }

    [Fact]
    public async Task ErrorNotification_FailsTheAction_AndExpectingErrorPinsIt()
    {
        var failed = await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            ACTION StreamProgress (Fail="true")
            """);
        Assert.False(failed.Ok);
        Assert.Contains(AllDiagnostics(failed), d => d.Kind == ErrorKind.AssertNotificationError && d.Message == "Processing failed.");

        var pinned = await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            ACTION StreamProgress (Fail="true") EXPECTING ERROR
            EXPECT StreamingAction.Notification.Type = "Error"
            EXPECT StreamingAction.Messages = "No products selected."
            """);
        Assert.True(pinned.Ok, pinned.Describe());
    }

    [Fact]
    public async Task NotAllowedMessage_IsVisible()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            ACTION StreamProgress (Deny="true")
            EXPECT StreamingAction.Messages CONTAINS "not allowed"
            EXPECT StreamingAction.Notification IS NULL
            """);
        Assert.True(result.Ok, result.Describe());
    }

    [Fact]
    public async Task Capture_IsClearedByTheNextVerb()
    {
        var result = await Run("""
            SIGN-IN admin / admin
            OPEN MenuItem Home/Products
            EXPECT StreamingAction.Messages IS NULL
            ACTION StreamProgress
            EXPECT StreamingAction.Messages IS NOT NULL
            SEARCH "Widget"
            EXPECT StreamingAction.Messages IS NULL
            EXPECT StreamingAction.Notification IS NULL
            """);
        Assert.True(result.Ok, result.Describe());
    }
}
