using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Parsing;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>Parser coverage for the detail-attribute (AsDetail) row grammar: <c>DELETE-ROW Detail Attribute</c>
/// and <c>EXPECT Detail Attribute … TotalItems | ROW</c>. Lint-only; the runtime path is in the integration tests.</summary>
public sealed class DetailAttributeRowsLintTests
{
    private static ScriptAst Parse(string body)
    {
        var lexer = new Lexer(body, "<test>");
        var parser = new Parser(lexer.Tokenize(), lexer.Diagnostics);
        var ast = parser.Parse();
        Assert.True(parser.Diagnostics.Count == 0,
            $"Parse errors: {string.Join("; ", parser.Diagnostics.Select(d => d.Message))}");
        return ast;
    }

    private static T Single<T>(string body) where T : Statement =>
        Parse(body).Steps.SelectMany(s => s.Statements).OfType<T>().Single();

    [Fact]
    public void DeleteRow_Index_Parses()
    {
        var s = Single<DeleteRowStmt>("DELETE-ROW Detail Attribute \"Certificates\" 1");
        Assert.Equal("Certificates", s.AttributeName);
        Assert.NotNull(s.Index);
        Assert.Null(s.MatchColumn);
    }

    [Fact]
    public void DeleteRow_Where_Parses()
    {
        var s = Single<DeleteRowStmt>("DELETE-ROW Detail Attribute \"Certificates\" WHERE SerialNumber = \"0A1B\"");
        Assert.Equal("SerialNumber", s.MatchColumn);
        Assert.Equal(ExpectOp.Eq, s.MatchOp);
        Assert.Null(s.Index);
    }

    [Fact]
    public void Expect_TotalItems_Parses()
    {
        var s = Single<ExpectStmt>("EXPECT Detail Attribute \"Certificates\" TotalItems = 2");
        Assert.Equal(ExpectSubjectKind.DetailAttributeRows, s.Subject.Kind);
        Assert.Equal("Certificates", s.Subject.Name);
    }

    [Fact]
    public void Expect_RowCell_Parses()
    {
        var s = Single<ExpectStmt>("EXPECT Detail Attribute \"Certificates\" ROW 0 Name = \"root\"");
        Assert.Equal(ExpectSubjectKind.DetailAttributeCell, s.Subject.Kind);
        Assert.Equal("Name", s.Subject.MetadataKey);
        Assert.NotNull(s.Subject.RowIndex);
    }

    [Fact]
    public void AddRow_Parses() =>
        Assert.Equal("Lines", Single<AddRowStmt>("ADD-ROW Detail Attribute \"Lines\"").AttributeName);

    [Fact]
    public void AddRow_As_BindsIndexVar_AndLintsClean()
    {
        const string body = "ADD-ROW Detail Attribute \"Lines\" AS @line\nSET Detail Attribute \"Lines\" ROW {{line}} Quantity = 2";
        Assert.Equal("line", Single<AddRowStmt>("ADD-ROW Detail Attribute \"Lines\" AS @line").IndexVar);
        Assert.Empty(VidyanoScript.Lint(body));
    }

    [Fact]
    public void SetRowCell_Parses()
    {
        var s = Single<SetStmt>("SET Detail Attribute \"Lines\" ROW 1 Product = LOOKUP \"Widget\"");
        Assert.Equal("Lines", s.DetailAttribute);
        Assert.Equal("Product", s.Attribute);
        Assert.NotNull(s.RowIndex);
        Assert.Equal(ReferenceHintKind.Lookup, s.Hint);
    }

    [Fact]
    public void SetAttributeNamedDetail_StillParsesAsPlainSet()
    {
        var s = Single<SetStmt>("SET Detail = \"x\"");
        Assert.Equal("Detail", s.Attribute);
        Assert.Null(s.DetailAttribute);
    }

    [Fact]
    public void Requires_DetailAttribute_Parses() =>
        Single<RequiresStmt>("REQUIRES Detail Attribute \"Certificates\" TotalItems > 0");

    [Fact]
    public void DetailQueryNamedAttribute_StillParsesAsQuery()
    {
        // No string literal after `Attribute` → it is a detail query that happens to be named Attribute.
        var s = Single<ExpectStmt>("EXPECT Detail Attribute TotalItems = 1");
        Assert.Equal(ExpectSubjectKind.TotalItems, s.Subject.Kind);
        Assert.Equal("Attribute", s.Subject.DetailName);
    }

    [Theory]
    [InlineData("DELETE-ROW 0")]
    [InlineData("DELETE-ROW Detail \"Certificates\" 0")]
    [InlineData("DELETE-ROW Detail Attribute \"C\"")]
    [InlineData("DELETE-ROW Detail Attribute \"C\" WHERE X > 1")]
    [InlineData("EXPECT Detail Attribute \"C\" = 1")]
    [InlineData("EXPECT Detail Attribute \"C\" ROW 0")]
    [InlineData("ADD-ROW")]
    [InlineData("ADD-ROW Detail \"C\"")]
    [InlineData("ADD-ROW Detail Attribute \"C\" AS")]
    [InlineData("SET Detail Attribute \"C\" Name = 1")]
    public void Malformed_ReportsDiagnostic(string body) =>
        Assert.NotEmpty(VidyanoScript.Lint(body));
}
