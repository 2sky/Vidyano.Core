using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Diagnostics;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Parser/grammar coverage for bounded iteration — <c>REPEAT &lt;n&gt; [AS @i] … END</c> and
/// <c>FOR-EACH ROW [Detail "x"] [WHERE col = val] [AS @row] … END</c>. These tests never touch a
/// server: they assert what <see cref="VidyanoScript.Lint"/> accepts (zero diagnostics) or rejects
/// (a specific <see cref="ErrorKind"/>). EVENTUALLY is intentionally not part of this build and is
/// not exercised here.
/// </summary>
public sealed class BoundedIterationLintTests
{
    private static void AssertClean(string body)
    {
        var diags = VidyanoScript.Lint(body);
        Assert.True(diags.Count == 0,
            $"Expected no diagnostics, got: {string.Join("; ", diags.Select(d => $"{d.Kind}: {d.Message}"))}");
    }

    private static void AssertHasKind(string body, string kind)
    {
        var diags = VidyanoScript.Lint(body);
        Assert.True(diags.Any(d => d.Kind == kind),
            $"Expected a '{kind}' diagnostic, got: {(diags.Count == 0 ? "(none)" : string.Join("; ", diags.Select(d => $"{d.Kind}: {d.Message}")))}");
    }

    // --- REPEAT: valid forms ------------------------------------------------------------------

    [Fact]
    public void Repeat_Bare_Parses()
    {
        AssertClean("REPEAT 3\n  EDIT\nEND");
    }

    [Fact]
    public void Repeat_WithIndexVar_Parses()
    {
        AssertClean("REPEAT 3 AS @i\n  EDIT\nEND");
    }

    [Fact]
    public void Repeat_InterpolatedCount_Parses()
    {
        AssertClean("@n = 4\nREPEAT {{n}} AS @i\n  EDIT\nEND");
    }

    [Fact]
    public void Repeat_EmptyBody_Parses()
    {
        AssertClean("REPEAT 0\nEND");
    }

    [Fact]
    public void Repeat_LowerCaseKeywords_Parses()
    {
        AssertClean("repeat 3 as @i\n  EDIT\nend");
    }

    // --- FOR-EACH ROW: valid forms ------------------------------------------------------------

    [Fact]
    public void ForEach_Bare_Parses()
    {
        AssertClean("FOR-EACH ROW\n  EDIT\nEND");
    }

    [Fact]
    public void ForEach_WithWhere_Parses()
    {
        AssertClean("FOR-EACH ROW WHERE Status = \"Inactive\"\n  EDIT\nEND");
    }

    [Fact]
    public void ForEach_WithDetail_Parses()
    {
        AssertClean("FOR-EACH ROW Detail \"OrderLines\"\n  EDIT\nEND");
    }

    [Fact]
    public void ForEach_WithRowVar_Parses()
    {
        AssertClean("FOR-EACH ROW AS @row\n  EDIT\nEND");
    }

    [Fact]
    public void ForEach_WithDetailWhereAndRowVar_Parses()
    {
        AssertClean("FOR-EACH ROW Detail \"OrderLines\" WHERE Status = \"Inactive\" AS @row\n  EDIT\nEND");
    }

    [Fact]
    public void ForEach_OpenRowByRowHandle_Parses()
    {
        // OPEN-ROW @row inside the body opens by the snapshotted row identity the loop bound.
        AssertClean("FOR-EACH ROW AS @row\n  OPEN-ROW @row\nEND");
    }

    [Fact]
    public void ForEach_LowerCaseKeywords_Parses()
    {
        AssertClean("for-each row where Status = \"x\" as @row\n  EDIT\nend");
    }

    // --- Nesting (a loop inside a loop) -------------------------------------------------------

    [Fact]
    public void Repeat_NestedInsideForEach_Parses()
    {
        AssertClean("FOR-EACH ROW AS @row\n  REPEAT 2 AS @i\n    EDIT\n  END\nEND");
    }

    [Fact]
    public void Repeat_NestedInsideRepeat_Parses()
    {
        AssertClean("REPEAT 2 AS @outer\n  REPEAT 2 AS @inner\n    EDIT\n  END\nEND");
    }

    // --- Loop-bound variables don't trip the undeclared-variable lint -------------------------

    [Fact]
    public void Repeat_IndexVar_ReadInBody_NotFlaggedAsUndeclared()
    {
        // {{i}} reads the AS @i binding — the analyzer treats the loop binding as a declaration.
        AssertClean("REPEAT 3 AS @i\n  SET Name = \"Item {{i}}\"\nEND");
    }

    [Fact]
    public void ForEach_RowVar_CellReadInBody_NotFlaggedAsUndeclared()
    {
        // {{@row.Name}} is an @-prefixed scoped read, which the analyzer skips outright; this still
        // confirms the body parses and the read is accepted with the binding present.
        AssertClean("FOR-EACH ROW AS @row\n  SET Notes = \"{{@row.Name}}\"\nEND");
    }

    [Fact]
    public void IndexVar_ReadOutsideLoop_WithNoBinding_FlaggedAsUndeclared()
    {
        // No enclosing AS @i — a plain {{i}} read with nothing declaring it is the undeclared case.
        AssertHasKind("SET Name = \"Item {{i}}\"", ErrorKind.ResolveVariable);
    }

    [Fact]
    public void IndexVar_ReadAfterLoopCloses_StillAcceptedBecausePresenceOnly()
    {
        // The analyzer is presence-only (order-insensitive): a binding anywhere makes the read legal,
        // so a {{i}} after END is accepted. Documents the documented presence-only behavior, not a leak.
        AssertClean("REPEAT 3 AS @i\n  EDIT\nEND\nSET Name = \"{{i}}\"");
    }

    // --- Error cases --------------------------------------------------------------------------

    [Fact]
    public void Repeat_MissingEnd_AtEof_IsMissingBlockEnd()
    {
        AssertHasKind("REPEAT 3\n  EDIT", ErrorKind.ParseMissingBlockEnd);
    }

    [Fact]
    public void ForEach_MissingEnd_AtEof_IsMissingBlockEnd()
    {
        AssertHasKind("FOR-EACH ROW\n  EDIT", ErrorKind.ParseMissingBlockEnd);
    }

    [Fact]
    public void End_WithNoOpenBlock_IsUnexpectedToken()
    {
        AssertHasKind("END", ErrorKind.ParseUnexpectedToken);
    }

    [Fact]
    public void StepHeader_InsideBlock_IsParseError()
    {
        AssertHasKind("REPEAT 2\n  ### nested step\n  EDIT\nEND", ErrorKind.ParseUnexpectedToken);
    }

    [Fact]
    public void Requires_InsideBlock_IsParseError()
    {
        // Decision (f): the REQUIRES/CLEANUP gate model is top-level only.
        AssertHasKind("REPEAT 2\n  REQUIRES TotalItems >= 1\n  EDIT\nEND", ErrorKind.ParseUnexpectedToken);
    }

    [Fact]
    public void Cleanup_InsideBlock_IsParseError()
    {
        AssertHasKind("REPEAT 2\n  CLEANUP\n  EDIT\nEND", ErrorKind.ParseUnexpectedToken);
    }

    [Fact]
    public void ForEach_MissingRowKeyword_IsParseError()
    {
        AssertHasKind("FOR-EACH WHERE Status = \"x\"\n  EDIT\nEND", ErrorKind.ParseExpected);
    }

    [Fact]
    public void ForEach_WhereNonEqualsOperator_IsParseError()
    {
        AssertHasKind("FOR-EACH ROW WHERE Age >= 5\n  EDIT\nEND", ErrorKind.ParseExpected);
    }

    [Fact]
    public void Repeat_MissingCount_IsParseError()
    {
        AssertHasKind("REPEAT\n  EDIT\nEND", ErrorKind.ParseExpected);
    }

    // --- Catalog / classification of the new words --------------------------------------------

    [Fact]
    public void Repeat_And_ForEach_AreInVerbCatalog()
    {
        Assert.True(VerbCatalog.TryGet("REPEAT", out _));
        Assert.True(VerbCatalog.TryGet("FOR-EACH", out _));
    }

    [Fact]
    public void End_And_Row_ClassifyAsSubKeywords()
    {
        Assert.Equal(SemanticCategory.SubKeyword, KeywordCatalog.Classify("END"));
        Assert.Equal(SemanticCategory.SubKeyword, KeywordCatalog.Classify("ROW"));
    }

    [Fact]
    public void Repeat_And_ForEach_ClassifyAsVerbs()
    {
        Assert.Equal(SemanticCategory.Verb, KeywordCatalog.Classify("REPEAT"));
        Assert.Equal(SemanticCategory.Verb, KeywordCatalog.Classify("FOR-EACH"));
    }
}
