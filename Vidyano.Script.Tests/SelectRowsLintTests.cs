using System;
using System.IO;
using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Parsing;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Parser/grammar coverage for SELECT-ROWS, EXPECT Selection.Count, SIGN-IN FROM ENV, and {{env:...}}
/// value sourcing. Lint-only — never touches a server. The env: hole and FROM ENV resolve at run time,
/// so they parse clean regardless of the environment; their runtime behavior is in
/// <see cref="EnvSourcingTests"/>.
/// </summary>
public sealed class SelectRowsLintTests
{
    private static void AssertClean(string body)
    {
        var diags = VidyanoScript.Lint(body);
        Assert.True(diags.Count == 0,
            $"Expected no diagnostics, got: {string.Join("; ", diags.Select(d => $"{d.Kind}: {d.Message}"))}");
    }

    private static void AssertHasDiagnostic(string body)
    {
        var diags = VidyanoScript.Lint(body);
        Assert.True(diags.Count > 0, "Expected at least one parse diagnostic, got none.");
    }

    private static ScriptAst Parse(string body)
    {
        var lexer = new Lexer(body, "<test>");
        var parser = new Parser(lexer.Tokenize(), lexer.Diagnostics);
        var ast = parser.Parse();
        Assert.True(parser.Diagnostics.Count == 0,
            $"Parse errors: {string.Join("; ", parser.Diagnostics.Select(d => d.Message))}");
        return ast;
    }

    private static SelectRowsStmt FirstSelect(ScriptAst ast) =>
        ast.Steps.SelectMany(s => s.Statements).OfType<SelectRowsStmt>().Single();

    // --- SELECT-ROWS parse coverage -----------------------------------------------------------

    [Fact] public void SelectRows_All_Parses() => AssertClean("SELECT-ROWS ALL");
    [Fact] public void SelectRows_None_Parses() => AssertClean("SELECT-ROWS NONE");
    [Fact] public void SelectRows_Index_Parses() => AssertClean("SELECT-ROWS 0");
    [Fact] public void SelectRows_Where_Parses() => AssertClean("SELECT-ROWS WHERE Status = \"Open\"");
    [Fact] public void SelectRows_CaseInsensitiveKeyword_Parses() => AssertClean("select-rows all");

    [Fact] public void SelectRows_DetailAll_Parses() => AssertClean("SELECT-ROWS Detail \"OrderLines\" ALL");
    [Fact] public void SelectRows_DetailWhere_Parses() => AssertClean("SELECT-ROWS Detail \"OrderLines\" WHERE Sku = \"X\"");
    [Fact] public void SelectRows_DetailIndex_Parses() => AssertClean("SELECT-ROWS Detail \"OrderLines\" 0");
    [Fact] public void SelectRows_DetailNone_Parses() => AssertClean("SELECT-ROWS Detail \"OrderLines\" NONE");

    [Theory]
    [InlineData("SELECT-ROWS WHERE Status CONTAINS \"x\"")]
    [InlineData("SELECT-ROWS WHERE Age >= 5")]
    public void SelectRows_NonEqualsOperator_Diagnoses(string body) => AssertHasDiagnostic(body);

    [Fact] public void SelectRows_Where_MissingValue_Diagnoses() => AssertHasDiagnostic("SELECT-ROWS WHERE Status =");
    [Fact] public void SelectRows_Where_MissingColumn_Diagnoses() => AssertHasDiagnostic("SELECT-ROWS WHERE = \"Open\"");

    // --- SELECT-ROWS AST shape ----------------------------------------------------------------

    [Fact]
    public void SelectRows_All_ShapesAllFlag()
    {
        var sr = FirstSelect(Parse("SELECT-ROWS ALL"));
        Assert.True(sr.All);
        Assert.False(sr.None);
        Assert.Null(sr.Index);
        Assert.Null(sr.MatchColumn);
        Assert.Null(sr.DetailName);
    }

    [Fact]
    public void SelectRows_None_ShapesNoneFlag()
    {
        var sr = FirstSelect(Parse("SELECT-ROWS NONE"));
        Assert.True(sr.None);
        Assert.False(sr.All);
    }

    [Fact]
    public void SelectRows_DetailWhere_ShapesDetailAndColumn()
    {
        var sr = FirstSelect(Parse("SELECT-ROWS Detail \"OrderLines\" WHERE Sku = \"X\""));
        Assert.Equal("OrderLines", sr.DetailName);
        Assert.Equal("Sku", sr.MatchColumn);
        Assert.Equal(ExpectOp.Eq, sr.MatchOp);
        Assert.NotNull(sr.MatchValue);
        Assert.False(sr.All);
        Assert.False(sr.None);
    }

    [Fact]
    public void SelectRows_DetailAll_ShapesDetailAndAll()
    {
        var sr = FirstSelect(Parse("SELECT-ROWS Detail \"OrderLines\" ALL"));
        Assert.Equal("OrderLines", sr.DetailName);
        Assert.True(sr.All);
    }

    // --- EXPECT Selection.Count ---------------------------------------------------------------

    [Fact] public void Expect_SelectionCount_Eq_Parses() => AssertClean("EXPECT Selection.Count = 3");
    [Fact] public void Expect_SelectionCount_Gte_Parses() => AssertClean("EXPECT Selection.Count >= 1");
    [Fact] public void Expect_SelectionCount_DetailRedirect_Parses() => AssertClean("EXPECT Detail \"OrderLines\" Selection.Count > 0");

    // --- SIGN-IN FROM ENV ---------------------------------------------------------------------

    [Fact] public void SignIn_FromEnv_Parses() => AssertClean("SIGN-IN FROM ENV");
    [Fact] public void SignIn_FromEnv_Language_Parses() => AssertClean("SIGN-IN FROM ENV LANGUAGE nl-NL");

    [Fact]
    public void SignIn_FromEnv_ShapesFromEnvFlag()
    {
        var si = Parse("SIGN-IN FROM ENV").Steps.SelectMany(s => s.Statements).OfType<SignInStmt>().Single();
        Assert.True(si.FromEnv);
        Assert.Null(si.UserName);
        Assert.Null(si.Password);
    }

    // --- {{env:NAME}} value sourcing (parse only) ---------------------------------------------

    [Fact] public void Env_Hole_InSignIn_Parses() => AssertClean("SIGN-IN {{env:VIDYANO_USER}} / {{env:SVC_PW}}");
    [Fact] public void Env_Hole_WithFallback_Parses() => AssertClean("SET Owner = {{env:OWNER ?? \"unassigned\"}}");
    [Fact] public void Env_Hole_InExpect_Parses() => AssertClean("EXPECT {{env:REGION ?? \"eu\"}} IS NOT NULL");

    // --- Shipped samples lint clean -----------------------------------------------------------

    [Theory]
    [InlineData("selection.visc")]
    [InlineData("env-signin.visc")]
    public void Sample_LintsClean(string fileName)
    {
        var path = SamplePath(fileName);
        Assert.True(File.Exists(path), $"Sample not found at {path}");
        var diags = VidyanoScript.Lint(File.ReadAllText(path), path);
        Assert.True(diags.Count == 0,
            $"{fileName} should lint clean, got: {string.Join("; ", diags.Select(d => $"{d.Kind}: {d.Message}"))}");
    }

    private static string SamplePath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Vidyano.Core.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Vidyano.Script.Tool", "samples", fileName);
    }
}
