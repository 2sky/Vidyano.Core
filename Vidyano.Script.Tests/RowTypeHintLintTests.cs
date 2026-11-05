using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Parsing;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Parse shapes for <c>EXPECT [Detail "…"] Row [Column X] TYPEHINT key</c>. Evaluation needs a live query and runs in the
/// integration tests (<c>RowTypeHintTests</c>).
/// </summary>
public sealed class RowTypeHintLintTests
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
    [InlineData("EXPECT Row TYPEHINT foreground = \"red\"", null, "foreground", null)]
    [InlineData("EXPECT row typehint \"foreground\" IS NULL", null, "foreground", null)]
    [InlineData("EXPECT Row Column Amount TYPEHINT foreground = \"red\"", "Amount", "foreground", null)]
    [InlineData("EXPECT Detail \"Lines\" Row TYPEHINT foreground = \"red\"", null, "foreground", "Lines")]
    [InlineData("EXPECT Detail \"Lines\" Row Column Amount TYPEHINT foreground = \"red\"", "Amount", "foreground", "Lines")]
    public void RowTypeHint(string body, string? column, string key, string? detail)
    {
        var e = ParseExpect(body);
        Assert.Equal(ExpectSubjectKind.RowTypeHint, e.Subject.Kind);
        Assert.Equal(column, e.Subject.Name);
        Assert.Equal(key, e.Subject.MetadataKey);
        Assert.Equal(detail, e.Subject.DetailName);
    }

    [Fact]
    public void AttributeNamedRow_StillParsesAsAttribute()
    {
        var e = ParseExpect("EXPECT Row = \"3\"");
        Assert.NotEqual(ExpectSubjectKind.RowTypeHint, e.Subject.Kind);
        Assert.Equal("Row", e.Subject.Name);
    }

    [Fact]
    public void Requires_AcceptsRowTypeHint() =>
        Assert.Empty(VidyanoScript.Lint("REQUIRES Row TYPEHINT foreground = \"red\""));

    [Theory]
    [InlineData("EXPECT Row Column TYPEHINT foreground = \"red\"")]
    [InlineData("EXPECT Row Column Amount = \"red\"")]
    [InlineData("EXPECT Row TYPEHINT = \"red\"")]
    public void Malformed_IsAParseError(string body) =>
        Assert.NotEmpty(VidyanoScript.Lint(body));
}
