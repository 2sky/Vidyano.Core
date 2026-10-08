using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Parsing;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Parse shapes for <c>IS [NOT] PRESENT</c> — the one assertion a missing attribute / query column satisfies.
/// Evaluation needs a live PO / query and runs in the integration tests (<c>PresenceTests</c>).
/// </summary>
public sealed class PresentFlagLintTests
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
    [InlineData("EXPECT Attribute Price IS PRESENT", ExpectOp.Is)]
    [InlineData("EXPECT Attribute Price IS NOT PRESENT", ExpectOp.IsNot)]
    public void Attribute_IsPresent(string body, ExpectOp op)
    {
        var e = ParseExpect(body);
        Assert.Equal(ExpectSubjectKind.AttributeFlag, e.Subject.Kind);
        Assert.Equal(AttributeFlagKind.Present, e.Subject.Flag);
        Assert.Equal("Price", e.Subject.Name);
        Assert.Equal(op, e.Op);
    }

    [Fact]
    public void ScopedAttribute_IsPresent()
    {
        var e = ParseExpect("EXPECT Attribute @session.Region IS NOT PRESENT");
        Assert.Equal("session", e.Subject.Scope);
        Assert.Equal(AttributeFlagKind.Present, e.Subject.Flag);
    }

    [Fact]
    public void LeaflessColumn_IsNotPresent()
    {
        var e = ParseExpect("EXPECT Query.Columns[Price] IS NOT PRESENT");
        Assert.Equal(ExpectSubjectKind.QueryColumn, e.Subject.Kind);
        Assert.Equal("Price", e.Subject.Name);
        Assert.Null(e.Subject.MetadataKey);
        Assert.Equal(AttributeFlagKind.Present, e.Subject.Flag);
        Assert.Equal(ExpectOp.IsNot, e.Op);
    }

    [Fact]
    public void DetailColumn_IsPresent()
    {
        var e = ParseExpect("EXPECT Detail \"Prices\" Query.Columns[Price] IS PRESENT");
        Assert.Equal("Prices", e.Subject.DetailName);
        Assert.Equal(AttributeFlagKind.Present, e.Subject.Flag);
    }

    [Fact]
    public void ColumnWithLeaf_StillCompares()
    {
        var e = ParseExpect("EXPECT Query.Columns[Price].Label = \"Price\"");
        Assert.Equal("Label", e.Subject.MetadataKey);
        Assert.Equal(ExpectOp.Eq, e.Op);
    }

    [Fact]
    public void Requires_AcceptsPresent() =>
        Assert.Empty(VidyanoScript.Lint("REQUIRES Attribute Price IS PRESENT"));

    [Theory]
    [InlineData("EXPECT Query.Columns[Price] = \"x\"")]          // leafless: nothing to compare
    [InlineData("EXPECT Query.Columns[Price] IS NULL")]          // leafless: only PRESENT
    [InlineData("EXPECT Query.Columns[Price] IS VISIBLE")]
    [InlineData("EXPECT Query.Columns[Price].Label IS PRESENT")] // presence is the leafless form
    [InlineData("EXPECT Action Delete IS PRESENT")]              // actions use IS [NOT] AVAILABLE
    [InlineData("EXPECT Detail \"Lines\" IS PRESENT")]
    public void Rejected(string body) =>
        Assert.Contains(VidyanoScript.Lint(body), d => d.Kind is ErrorKind.ParseUnexpectedToken or ErrorKind.ParseExpected);
}
