using System.Linq;
using Vidyano;
using Vidyano.Script;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Parsing;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Coverage for <c>TranslatedString</c> support: the ported <see cref="Vidyano.TranslatedString"/> wire-format
/// type (the <c>{"en":"…","nl":"…"}</c> round-trip + fallback), and the parser shape of the
/// <c>SET/EXPECT &lt;attr&gt; LANGUAGE &lt;lang&gt; = &lt;value&gt;</c> grammar. The attribute read/write helpers and
/// the server merge need a live PO, so that round-trip is exercised by the Demo, not here.
/// </summary>
public sealed class TranslatedStringTests
{
    // --- parser shape -------------------------------------------------------------------------

    private static T SingleStatement<T>(string body) where T : Statement
    {
        var lexer = new Lexer(body, "<test>");
        var parser = new Parser(lexer.Tokenize(), lexer.Diagnostics);
        var ast = parser.Parse();
        Assert.True(parser.Diagnostics.Count == 0,
            $"Parse errors: {string.Join("; ", parser.Diagnostics.Select(d => d.Message))}");
        var stmts = ast.Steps.SelectMany(s => s.Statements).ToList();
        Assert.Single(stmts);
        return Assert.IsType<T>(stmts[0]);
    }

    [Fact]
    public void SetLanguage_ParsesWithLanguageExpression()
    {
        var stmt = SingleStatement<SetStmt>("SET Title LANGUAGE nl = \"Hulpmiddel\"");
        Assert.Equal("Title", stmt.Attribute);
        Assert.Null(stmt.Hint);
        Assert.Equal(SetValueKind.Value, stmt.ValueKind);
        var lang = Assert.IsType<IdentifierExpr>(stmt.Language);
        Assert.Equal("nl", lang.Name);
        var value = Assert.IsType<LiteralExpr>(stmt.Value);
        Assert.Equal("Hulpmiddel", value.Value);
    }

    [Fact]
    public void SetBare_HasNoLanguage()
    {
        // Regression: a bare SET (no LANGUAGE clause) leaves Language null — the current-language path.
        var stmt = SingleStatement<SetStmt>("SET Title = \"Widget\"");
        Assert.Null(stmt.Language);
    }

    [Fact]
    public void SetLanguage_WithFileRhs_IsParseError()
    {
        var diags = VidyanoScript.Lint("SET Title LANGUAGE nl = FILE \"x.bin\"");
        Assert.NotEmpty(diags);
        Assert.Contains(diags, d => d.Message.Contains("LANGUAGE"));
    }

    [Fact]
    public void SetLanguage_WithIdRhs_IsParseError()
    {
        var diags = VidyanoScript.Lint("SET Ref LANGUAGE nl = ID \"x\"");
        Assert.NotEmpty(diags);
    }

    [Fact]
    public void SetLanguage_LintsCleanOnArbitraryAttribute()
    {
        Assert.Empty(VidyanoScript.Lint("SET Whatever LANGUAGE nl = \"x\""));
    }

    [Fact]
    public void ExpectLanguage_ParsesAsAttributeSubjectWithLanguage()
    {
        var stmt = SingleStatement<ExpectStmt>("EXPECT Title LANGUAGE nl = \"Hulpmiddel\"");
        Assert.Equal(ExpectSubjectKind.Attribute, stmt.Subject.Kind);
        Assert.Equal("Title", stmt.Subject.Name);
        var lang = Assert.IsType<IdentifierExpr>(stmt.Subject.Language);
        Assert.Equal("nl", lang.Name);
        Assert.Equal(ExpectOp.Eq, stmt.Op);
        var value = Assert.IsType<LiteralExpr>(stmt.Value);
        Assert.Equal("Hulpmiddel", value.Value);
    }

    [Fact]
    public void ExpectLanguage_WithIdKeyword_IsParseError()
    {
        // LANGUAGE compares a translation; a translation has no document id, so `= ID` can't combine.
        var diags = VidyanoScript.Lint("EXPECT Title LANGUAGE nl = ID \"x\"");
        Assert.NotEmpty(diags);
    }

    [Fact]
    public void ExpectLanguage_LintsCleanOnArbitraryAttribute()
    {
        Assert.Empty(VidyanoScript.Lint("EXPECT Whatever LANGUAGE nl = \"x\""));
    }

    [Fact]
    public void ExpectBare_HasNoLanguage()
    {
        var stmt = SingleStatement<ExpectStmt>("EXPECT Title = \"Widget\"");
        Assert.Null(stmt.Subject.Language);
    }

    // --- Vidyano.TranslatedString (ported wire-format type) -----------------------------------

    [Fact]
    public void FromJson_ParsesLanguageMap()
    {
        var ts = TranslatedString.FromJson("{\"en\":\"Widget\",\"nl\":\"Hulpmiddel\"}");
        Assert.NotNull(ts);
        Assert.Equal("Widget", ts!["en"]);
        Assert.Equal("Hulpmiddel", ts["nl"]);
        Assert.Equal(new[] { "en", "nl" }, ts.Languages.ToArray());
    }

    [Fact]
    public void RoundTripsThroughJson()
    {
        var ts = TranslatedString.FromJson("{\"en\":\"Widget\",\"nl\":\"Hulpmiddel\",\"de\":\"Werkzeug\"}");
        var reparsed = TranslatedString.FromJson(ts!.ToString());
        Assert.Equal(ts, reparsed);
    }

    [Fact]
    public void FromJson_NullOrEmptyOrNonObject_IsNull()
    {
        Assert.Null(TranslatedString.FromJson(null));
        Assert.Null(TranslatedString.FromJson(""));
        Assert.Null(TranslatedString.FromJson("not json"));
    }

    [Fact]
    public void Indexer_MissingLanguage_IsEmptyString_AndCaseInsensitive()
    {
        var ts = TranslatedString.FromJson("{\"nl\":\"Hulpmiddel\"}")!;
        Assert.Equal(string.Empty, ts["fr"]);   // absent — never null, never throws
        Assert.Equal("Hulpmiddel", ts["NL"]);    // matched case-insensitively
    }

    [Fact]
    public void GetTranslation_UsesRequestedLanguage_ThenSingleLanguageFallback()
    {
        var multi = TranslatedString.FromJson("{\"en\":\"Widget\",\"nl\":\"Hulpmiddel\"}")!;
        Assert.Equal("Hulpmiddel", multi.GetTranslation("nl"));
        Assert.Equal(string.Empty, multi.GetTranslation("xx")); // unknown, multiple languages → empty

        var single = TranslatedString.FromJson("{\"en\":\"Widget\"}")!;
        Assert.Equal("Widget", single.GetTranslation("xx"));    // single-language optimization
    }

    [Fact]
    public void IsEmpty_ReflectsContent()
    {
        Assert.True(new TranslatedString().IsEmpty);
        Assert.True(TranslatedString.FromJson("{\"en\":\"\",\"nl\":\"\"}")!.IsEmpty);
        Assert.False(TranslatedString.FromJson("{\"en\":\"Widget\"}")!.IsEmpty);
    }

    [Fact]
    public void Equality_IsOrderIndependent_AndValueSensitive()
    {
        var a = TranslatedString.FromJson("{\"en\":\"Widget\",\"nl\":\"Hulpmiddel\"}");
        var b = TranslatedString.FromJson("{\"nl\":\"Hulpmiddel\",\"en\":\"Widget\"}");
        var c = TranslatedString.FromJson("{\"en\":\"Widget\",\"nl\":\"Anders\"}");
        Assert.Equal(a, b);
        Assert.Equal(a!.GetHashCode(), b!.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Indexer_Set_StoresNullAsEmpty()
    {
        var ts = new TranslatedString { ["en"] = "Widget" };
        ts["en"] = null;
        Assert.Equal(string.Empty, ts["en"]);
    }

    [Fact]
    public void ImplicitString_IsJsonWireForm()
    {
        var ts = TranslatedString.FromJson("{\"en\":\"Widget\"}");
        string? s = ts;
        Assert.Equal("{\"en\":\"Widget\"}", s);
        string? none = (TranslatedString?)null;
        Assert.Null(none);
    }
}
