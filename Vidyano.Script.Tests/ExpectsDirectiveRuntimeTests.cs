using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vidyano.Script.Parsing;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Interpreter behavior for the <c>@expects a, b</c> directive, exercised WITHOUT a live server (the
/// directive and the assignments/EXPECTs used here resolve before any network call). These pin the
/// contract that distinguishes <c>@expects</c> from the <c>@x = "{{x}}"</c> self-assign hack it replaces:
/// the directive is a pure no-op — it never writes the variable table, so a host-supplied value is
/// REQUIRED at run time and is NEVER overwritten, while an unsupplied declared name still loud-fails.
/// </summary>
public sealed class ExpectsDirectiveRuntimeTests
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

    /// <summary>A session pointed at an unreachable host. Safe: no network happens until a verb talks to
    /// the server, and these tests deliberately avoid those.</summary>
    private static VidyanoSession NewSession() =>
        new("https://127.0.0.1:1", acceptAnyServerCertificate: true);

    private static Interpreter NewInterpreter(
        VidyanoSession session,
        IReadOnlyDictionary<string, object?>? initialVars = null) =>
        new(TestSessionBook.Wrap(session), initialVars: initialVars, mode: GuardMode.Navigation, tools: null, cancellationToken: default);

    private static List<StatementResult> Statements(ScriptResult result) =>
        result.Steps.SelectMany(s => s.Statements).ToList();

    [Fact]
    public async Task Expects_IsItselfANoOpPass()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session);
        var result = await interp.RunAsync(Parse("@expects region, tenant"));
        var stmt = Statements(result).Single(s => s.Statement is ExpectsDirective);
        Assert.True(stmt.Ok, "@expects is a no-op declaration and must pass.");
    }

    [Fact]
    public async Task Expects_DoesNotPopulateVariableTable()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session);
        // The whole point: declaring a variable must NOT bind it — the host still has to supply it.
        var result = await interp.RunAsync(Parse("@expects region"));
        Assert.True(result.Ok);
        Assert.False(interp.Variables.ContainsKey("region"),
            "@expects must not write the variable table; the host is the only source of the value.");
    }

    [Fact]
    public async Task Expects_UnsuppliedVar_StillLoudFailsAtFirstUse()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session); // no host value for `region`
        // The declaration silences the static lint, but the runtime backstop is unchanged: reading an
        // unsupplied declared variable still fails loudly (resolve-variable), here in the assignment.
        var result = await interp.RunAsync(Parse("@expects region\n@x = \"{{region}}\""));
        var assign = Statements(result).Single(s => s.Statement is VariableAssignment);
        Assert.False(assign.Ok, "An unsupplied @expects variable must still loud-fail when first read.");
        Assert.Contains(assign.Diagnostics, d => d.Kind == "resolve-variable");
    }

    [Fact]
    public async Task Expects_DoesNotOverwriteHostSuppliedValue()
    {
        using var session = NewSession();
        var initialVars = new Dictionary<string, object?> { ["region"] = "eu" };
        var interp = NewInterpreter(session, initialVars: initialVars);
        // Unlike `@region = "{{region}}"`, the directive leaves the host value intact and readable.
        var result = await interp.RunAsync(Parse("@expects region\nEXPECT {{region}} = \"eu\""));
        Assert.True(Statements(result).Last().Ok, "The host-supplied value must flow through untouched.");
        Assert.Equal("eu", interp.Variables["region"]);
    }
}
