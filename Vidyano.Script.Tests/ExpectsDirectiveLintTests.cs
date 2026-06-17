using System.Linq;
using Vidyano.Script;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Parsing;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Parser/grammar coverage for the <c>@expects a, b</c> directive — the runtime-safe in-file
/// declaration of host-supplied variables. These tests never touch a server: they assert what
/// <see cref="VidyanoScript.Lint"/> accepts (zero diagnostics) or rejects (a specific
/// <see cref="ErrorKind"/>), plus the parsed AST shape. The runtime no-op guarantee (the directive
/// never writes the variable table) lives in <see cref="ExpectsDirectiveRuntimeTests"/>.
/// </summary>
public sealed class ExpectsDirectiveLintTests
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

    // --- The core fix: a host-injected {{x}} declared via @expects no longer false-positives --------

    [Fact]
    public void Expects_SingleName_SilencesUndeclaredRead()
    {
        // Without the @expects line this read would be flagged `resolve-variable`; with it, it's clean —
        // even though nothing in the script assigns `region` and Lint() gets no expectedVariables.
        AssertClean("@expects region\nSET Name = \"{{region}}\"");
    }

    [Fact]
    public void Expects_MultipleNames_SilenceAllReads()
    {
        AssertClean("@expects importFile, fileName\nSET Name = \"{{importFile}}-{{fileName}}\"");
    }

    [Fact]
    public void Expects_IsCaseInsensitive()
    {
        AssertClean("@EXPECTS region\nSET Name = \"{{region}}\"");
    }

    [Fact]
    public void Expects_DoesNotDeclareUnlistedNames()
    {
        // `region` is declared, `tenant` is not — only the undeclared read is flagged.
        AssertHasKind("@expects region\nSET Name = \"{{tenant}}\"", ErrorKind.ResolveVariable);
    }

    [Fact]
    public void Expects_IsPresenceOnly_DeclarationAfterUseStillCounts()
    {
        // The analyzer is presence-only (order-insensitive), like loop bindings — a declaration anywhere
        // in the script makes the read legal, so authors aren't forced to put @expects at the very top.
        AssertClean("SET Name = \"{{region}}\"\n@expects region");
    }

    [Fact]
    public void Expects_ComposesWithAssignedVars()
    {
        AssertClean("@expects region\n@id = {{@uuid}}\nSET Name = \"{{region}}-{{id}}\"");
    }

    // --- Parse errors -------------------------------------------------------------------------------

    [Fact]
    public void Expects_NoNames_IsParseError()
    {
        AssertHasKind("@expects", ErrorKind.ParseExpected);
    }

    [Fact]
    public void Expects_TrailingComma_IsParseError()
    {
        AssertHasKind("@expects region,", ErrorKind.ParseExpected);
    }

    [Fact]
    public void Expects_MissingComma_IsParseError()
    {
        AssertHasKind("@expects region tenant", ErrorKind.ParseExpected);
    }

    // --- AST shape ----------------------------------------------------------------------------------

    [Fact]
    public void Expects_ParsesToExpectedShape()
    {
        var lexer = new Lexer("@expects importFile, fileName", "<test>");
        var parser = new Parser(lexer.Tokenize(), lexer.Diagnostics);
        var ast = parser.Parse();
        Assert.True(parser.Diagnostics.Count == 0,
            $"Parse errors: {string.Join("; ", parser.Diagnostics.Select(d => d.Message))}");
        var ed = ast.Steps.SelectMany(s => s.Statements).OfType<ExpectsDirective>().Single();
        Assert.Equal(new[] { "importFile", "fileName" }, ed.Names);
    }
}
