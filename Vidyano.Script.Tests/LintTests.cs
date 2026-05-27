using System;
using System.IO;
using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Diagnostics;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Parser/grammar coverage for the determinism feature set. These tests never touch a server: they
/// assert what <see cref="VidyanoScript.Lint"/> accepts (zero diagnostics) or rejects (a diagnostic).
/// </summary>
public sealed class LintTests
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

    // --- REQUIRES (data/state form) -----------------------------------------------------------

    [Fact]
    public void Requires_DataGate_Comparison_Parses()
    {
        AssertClean("REQUIRES TotalItems >= 1");
    }

    [Fact]
    public void Requires_DataGate_ScopedIsNotNull_Parses()
    {
        AssertClean("REQUIRES @session IS NOT NULL");
    }

    [Fact]
    public void Requires_DataGate_BareNameComparison_Parses()
    {
        AssertClean("REQUIRES Status = \"Approved\"");
    }

    // --- REQUIRES TOOL (capability form) ------------------------------------------------------

    [Fact]
    public void Requires_Tool_Parses()
    {
        AssertClean("REQUIRES TOOL seed");
    }

    [Fact]
    public void Requires_Tool_HyphenatedName_Parses()
    {
        AssertClean("REQUIRES TOOL seed-db");
    }

    [Fact]
    public void Requires_Tool_MissingName_Diagnoses()
    {
        AssertHasDiagnostic("REQUIRES TOOL");
    }

    [Fact]
    public void Requires_MissingSubject_Diagnoses()
    {
        AssertHasDiagnostic("REQUIRES");
    }

    // --- CLEANUP ------------------------------------------------------------------------------

    [Fact]
    public void Cleanup_Marker_Parses()
    {
        AssertClean("CLEANUP");
    }

    [Fact]
    public void Cleanup_WithFollowingStatements_Parses()
    {
        AssertClean("REQUIRES TOOL seed\nEDIT\nCLEANUP\nEXPECT NavStack.Depth >= 0");
    }

    // --- EXPECT ... MATCHES -------------------------------------------------------------------

    [Fact]
    public void Expect_Matches_Parses()
    {
        AssertClean("EXPECT Code MATCHES \"^[A-Z]{2}\\\\d+$\"");
    }

    [Fact]
    public void Expect_Matches_OnInterpolation_Parses()
    {
        AssertClean("EXPECT {{@uuid}} MATCHES \"^[0-9a-f-]{36}$\"");
    }

    [Fact]
    public void Expect_Matches_MissingPattern_Diagnoses()
    {
        AssertHasDiagnostic("EXPECT Code MATCHES");
    }

    [Fact]
    public void StringInterp_EmptyHole_Diagnoses()
    {
        AssertHasDiagnostic("@x = \"a{{}}b\"");
    }

    // --- Built-in vars parse as interpolations ------------------------------------------------

    [Theory]
    [InlineData("@today")]
    [InlineData("@now")]
    [InlineData("@uuid")]
    [InlineData("@random")]
    public void BuiltinVar_AsExpectSubject_Parses(string name)
    {
        AssertClean($"EXPECT {{{{{name}}}}} IS NOT NULL");
    }

    // --- OPEN-ROW WHERE (by-value row selector) -----------------------------------------------

    [Fact]
    public void OpenRowWhere_StringValue_Parses()
    {
        AssertClean("OPEN-ROW WHERE Name = \"Acme\"");
    }

    [Fact]
    public void OpenRowWhere_WithAsHandle_Parses()
    {
        AssertClean("OPEN-ROW WHERE Name = \"Acme\" AS @acme");
    }

    [Fact]
    public void OpenRowWhere_InterpolatedValue_Parses()
    {
        AssertClean("OPEN-ROW WHERE Name = \"{{@uuid}}\"");
    }

    [Fact]
    public void OpenRowWhere_DottedColumn_Parses()
    {
        AssertClean("OPEN-ROW WHERE Customer.Name = \"Acme\"");
    }

    [Fact]
    public void OpenRowWhere_CaseInsensitiveKeyword_Parses()
    {
        AssertClean("OPEN-ROW where Name = \"Acme\"");
    }

    [Fact]
    public void OpenRow_Positional_StillParses()
    {
        AssertClean("OPEN-ROW 0");
    }

    [Fact]
    public void OpenRow_PositionalWithAsHandle_StillParses()
    {
        AssertClean("OPEN-ROW 0 AS @h");
    }

    [Theory]
    [InlineData("OPEN-ROW WHERE Name CONTAINS \"Acme\"")]
    [InlineData("OPEN-ROW WHERE Age >= 5")]
    public void OpenRowWhere_NonEqualsOperator_Diagnoses(string body)
    {
        AssertHasDiagnostic(body);
    }

    [Fact]
    public void OpenRowWhere_MissingValue_Diagnoses()
    {
        AssertHasDiagnostic("OPEN-ROW WHERE Name =");
    }

    [Fact]
    public void OpenRowWhere_MissingColumn_Diagnoses()
    {
        AssertHasDiagnostic("OPEN-ROW WHERE = \"Acme\"");
    }

    // --- The shipped sample lints cleanly -----------------------------------------------------

    [Fact]
    public void DeterministicSample_LintsClean()
    {
        var path = SamplePath("deterministic.visc");
        Assert.True(File.Exists(path), $"Sample not found at {path}");
        var body = File.ReadAllText(path);
        var diags = VidyanoScript.Lint(body, path);
        Assert.True(diags.Count == 0,
            $"deterministic.visc should lint clean, got: {string.Join("; ", diags.Select(d => $"{d.Kind}: {d.Message}"))}");
    }

    private static string SamplePath(string fileName)
    {
        // Walk up from the test assembly's location to the repo root, then into the tool samples.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Vidyano.Core.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Vidyano.Script.Tool", "samples", fileName);
    }
}
