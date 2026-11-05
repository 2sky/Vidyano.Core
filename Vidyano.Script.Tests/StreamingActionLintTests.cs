using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Parsing;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Parse shapes for <c>EXPECT StreamingAction.Messages</c> / <c>.Notification</c> / <c>.Notification.Type</c>. Evaluation needs a
/// live streaming action and runs in the integration tests (<c>StreamingActionTests</c>).
/// </summary>
public sealed class StreamingActionLintTests
{
    private static ExpectStmt ParseExpect(string body)
    {
        var lexer = new Lexer(body, "<test>");
        var parser = new Parser(lexer.Tokenize(), lexer.Diagnostics);
        var stmt = Assert.Single(parser.Parse().Steps.SelectMany(s => s.Statements));
        Assert.True(parser.Diagnostics.Count == 0, string.Join("; ", parser.Diagnostics.Select(d => d.Message)));
        return Assert.IsType<ExpectStmt>(stmt);
    }

    [Theory]
    [InlineData("EXPECT StreamingAction.Messages CONTAINS \"done\"", ExpectSubjectKind.StreamingActionMessages)]
    [InlineData("EXPECT streamingaction.messages IS NULL", ExpectSubjectKind.StreamingActionMessages)]
    [InlineData("EXPECT StreamingAction.Notification = \"Sent 3 addenda.\"", ExpectSubjectKind.StreamingActionNotification)]
    [InlineData("EXPECT StreamingAction.Notification.Type = \"Notice\"", ExpectSubjectKind.StreamingActionNotificationType)]
    public void Subjects(string body, ExpectSubjectKind kind) =>
        Assert.Equal(kind, ParseExpect(body).Subject.Kind);

    [Fact]
    public void Requires_AcceptsStreamingAction() =>
        Assert.Empty(VidyanoScript.Lint("REQUIRES StreamingAction.Notification IS NOT NULL"));

    [Theory]
    [InlineData("EXPECT StreamingAction = \"x\"")]
    [InlineData("EXPECT StreamingAction.Title = \"x\"")]
    [InlineData("EXPECT StreamingAction.Notification.Kind = \"x\"")]
    public void Malformed_IsAParseError(string body) =>
        Assert.NotEmpty(VidyanoScript.Lint(body));
}
