using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Parsing;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Parse shapes for <c>EXPECT Attribute X OPTIONS [NOT] CONTAINS "…"</c> and <c>EXPECT Detail "…" Notification</c>.
/// Evaluation needs a live PO and runs in the integration tests (<c>OptionsAndDetailNotificationTests</c>).
/// </summary>
public sealed class OptionsAndDetailNotificationLintTests
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
    [InlineData("EXPECT Attribute Zone OPTIONS CONTAINS \"Flanders\"", ExpectOp.Contains)]
    [InlineData("EXPECT Attribute Zone OPTIONS NOT CONTAINS \"Flanders\"", ExpectOp.NotContains)]
    [InlineData("EXPECT Attribute Zone options contains \"Flanders\"", ExpectOp.Contains)]
    public void Options(string body, ExpectOp op)
    {
        var e = ParseExpect(body);
        Assert.Equal(ExpectSubjectKind.AttributeOptions, e.Subject.Kind);
        Assert.Equal("Zone", e.Subject.Name);
        Assert.Equal(op, e.Op);
    }

    [Fact]
    public void ScopedOptions()
    {
        var e = ParseExpect("EXPECT Attribute @session.Region OPTIONS CONTAINS \"{{region}}\"");
        Assert.Equal(ExpectSubjectKind.AttributeOptions, e.Subject.Kind);
        Assert.Equal("session", e.Subject.Scope);
    }

    [Fact]
    public void Requires_AcceptsOptions() =>
        Assert.Empty(VidyanoScript.Lint("REQUIRES Attribute Zone OPTIONS CONTAINS \"Flanders\""));

    [Theory]
    [InlineData("EXPECT Detail \"Lines\" Notification CONTAINS \"locked\"", ExpectSubjectKind.Notification)]
    [InlineData("EXPECT Detail \"Lines\" Notification IS NULL", ExpectSubjectKind.Notification)]
    [InlineData("EXPECT Detail \"Lines\" Notification.Type = \"Error\"", ExpectSubjectKind.NotificationType)]
    public void DetailNotification(string body, ExpectSubjectKind kind)
    {
        var e = ParseExpect(body);
        Assert.Equal(kind, e.Subject.Kind);
        Assert.Equal("Lines", e.Subject.DetailName);
    }

    [Theory]
    [InlineData("EXPECT Attribute Zone OPTIONS = \"Flanders\"")]
    [InlineData("EXPECT Attribute Zone OPTIONS MATCHES \"Fl.*\"")]
    [InlineData("EXPECT Attribute Zone OPTIONS IS NULL")]
    [InlineData("EXPECT Attribute Zone OPTIONS")]
    [InlineData("EXPECT Detail \"Lines\" IsDirty = true")] // Detail still rejects non-query subjects
    public void Rejected(string body) =>
        Assert.Contains(VidyanoScript.Lint(body), d => d.Kind is ErrorKind.ParseUnexpectedToken or ErrorKind.ParseExpected);
}
