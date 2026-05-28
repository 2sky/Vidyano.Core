using System.Linq;
using Vidyano.Script;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Parser/grammar coverage for three new <c>.visc</c> additions: the zero-arg <c>GO-BACK</c> verb,
/// the optional <c>Detail "&lt;name&gt;"</c> clause on <c>OPEN-ROW</c>, and the optional
/// <c>Detail "&lt;name&gt;"</c> prefix on <c>EXPECT</c> (which must be followed by a query-family
/// subject). These tests never touch a server: they assert what <see cref="VidyanoScript.Lint"/>
/// accepts (zero diagnostics) or rejects (a diagnostic).
/// </summary>
public sealed class DetailQueryAndGoBackLintTests
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

    // --- GO-BACK (zero-arg verb) --------------------------------------------------------------

    [Fact]
    public void GoBack_Parses()
    {
        AssertClean("GO-BACK");
    }

    [Fact]
    public void GoBack_LowerCase_Parses()
    {
        AssertClean("go-back");
    }

    // --- OPEN-ROW Detail "<name>" <selector> --------------------------------------------------

    [Fact]
    public void OpenRowDetail_StringNameWithIndex_Parses()
    {
        AssertClean("OPEN-ROW Detail \"OrderLines\" 0");
    }

    [Fact]
    public void OpenRowDetail_BareIdentifierName_Parses()
    {
        AssertClean("OPEN-ROW Detail OrderLines 0");
    }

    [Fact]
    public void OpenRowDetail_IndexWithAsHandle_Parses()
    {
        AssertClean("OPEN-ROW Detail \"OrderLines\" 0 AS @line");
    }

    [Fact]
    public void OpenRowDetail_Where_Parses()
    {
        AssertClean("OPEN-ROW Detail \"OrderLines\" WHERE Product = \"Widget\"");
    }

    [Fact]
    public void OpenRowDetail_WhereWithAsHandle_Parses()
    {
        AssertClean("OPEN-ROW Detail \"OrderLines\" WHERE Sku = \"A1\" AS @line");
    }

    [Fact]
    public void OpenRowDetail_CaseInsensitiveKeyword_Parses()
    {
        AssertClean("OPEN-ROW detail \"OrderLines\" 0");
    }

    [Fact]
    public void OpenRowDetail_MissingNameAndSelector_Diagnoses()
    {
        AssertHasDiagnostic("OPEN-ROW Detail");
    }

    [Fact]
    public void OpenRowDetail_MissingSelector_Diagnoses()
    {
        AssertHasDiagnostic("OPEN-ROW Detail \"OrderLines\"");
    }

    [Fact]
    public void OpenRowDetail_WhereNonEqualsOperator_Diagnoses()
    {
        AssertHasDiagnostic("OPEN-ROW Detail \"OrderLines\" WHERE Sku CONTAINS \"A1\"");
    }

    // --- OPEN-ROW pre-existing forms still parse (regression) ---------------------------------

    [Fact]
    public void OpenRow_Positional_StillParses()
    {
        AssertClean("OPEN-ROW 0");
    }

    [Fact]
    public void OpenRow_Where_StillParses()
    {
        AssertClean("OPEN-ROW WHERE Name = \"Acme\"");
    }

    // --- EXPECT Detail "<name>" <query-subject> -----------------------------------------------

    [Fact]
    public void ExpectDetail_TotalItemsEquals_Parses()
    {
        AssertClean("EXPECT Detail \"OrderLines\" TotalItems = 3");
    }

    [Fact]
    public void ExpectDetail_TotalItemsGreaterOrEqual_Parses()
    {
        AssertClean("EXPECT Detail \"OrderLines\" TotalItems >= 1");
    }

    [Fact]
    public void ExpectDetail_BareIdentifierName_Parses()
    {
        AssertClean("EXPECT Detail OrderLines TotalItems = 3");
    }

    [Fact]
    public void ExpectDetail_QueryHasSearched_Parses()
    {
        AssertClean("EXPECT Detail \"OrderLines\" Query.HasSearched = true");
    }

    [Fact]
    public void ExpectDetail_QueryColumnLabel_Parses()
    {
        AssertClean("EXPECT Detail \"OrderLines\" Query.Columns[Sku].Label = \"SKU\"");
    }

    [Fact]
    public void ExpectDetail_NonQueryBareAttribute_Diagnoses()
    {
        AssertHasDiagnostic("EXPECT Detail \"OrderLines\" Status = \"Approved\"");
    }

    [Fact]
    public void ExpectDetail_NonQueryIsInEdit_Diagnoses()
    {
        AssertHasDiagnostic("EXPECT Detail \"OrderLines\" IsInEdit = true");
    }

    [Fact]
    public void ExpectDetail_MissingName_Diagnoses()
    {
        AssertHasDiagnostic("EXPECT Detail");
    }

    [Fact]
    public void ExpectDetail_NestedDetail_Diagnoses()
    {
        AssertHasDiagnostic("EXPECT Detail \"X\" Detail \"Y\" TotalItems = 1");
    }
}
