using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vidyano.Script.Parsing;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Interpreter behavior for bounded iteration, exercised WITHOUT a live server. Only the parts of
/// REPEAT reachable before any navigation/server call are covered here: the entry-time bound check
/// (resolved from the count expression alone) and the zero-iteration case (an empty body that never
/// talks to the server). FOR-EACH and a populated REPEAT body need a live Query / signed-in session
/// to reach, so those stay at the parse level in <see cref="BoundedIterationLintTests"/> — there is
/// no mock server (VidyanoSession drives a real Client).
/// </summary>
public sealed class BoundedIterationRuntimeTests
{
    private static ScriptAst Parse(string body)
    {
        var lexer = new Lexer(body, "<test>");
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens, lexer.Diagnostics);
        var ast = parser.Parse();
        Assert.True(parser.Diagnostics.Count == 0,
            $"Parse errors: {string.Join("; ", parser.Diagnostics.Select(d => d.Message))}");
        return ast;
    }

    /// <summary>A session pointed at an unreachable host. Safe to construct: no network happens until
    /// a verb that talks to the server runs, and these tests deliberately avoid those.</summary>
    private static VidyanoSession NewSession() =>
        new("https://127.0.0.1:1", acceptAnyServerCertificate: true);

    private static Interpreter NewInterpreter(VidyanoSession session) =>
        new(TestSessionBook.Wrap(session), initialVars: null, mode: GuardMode.Navigation, tools: null, cancellationToken: default);

    private static List<StatementResult> Statements(ScriptResult result) =>
        result.Steps.SelectMany(s => s.Statements).ToList();

    // --- REPEAT bound checking (resolved at entry, server-free) -------------------------------

    [Fact]
    public async Task Repeat_NegativeCount_IsInvalidBound()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session);
        // The count resolves to a negative int before any body statement runs — invalid bound.
        var ast = Parse("@n = -1\nREPEAT {{n}}\n  EDIT\nEND");
        var result = await interp.RunAsync(ast);
        var repeat = Statements(result).Single(s => s.Statement is RepeatStmt);
        Assert.False(repeat.Ok, "A negative REPEAT count must fail.");
        Assert.Contains(repeat.Diagnostics, d => d.Kind == "state-invalid-bound");
    }

    [Fact]
    public async Task Repeat_NonIntegerCount_IsInvalidBound()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session);
        var ast = Parse("@n = \"abc\"\nREPEAT {{n}}\n  EDIT\nEND");
        var result = await interp.RunAsync(ast);
        var repeat = Statements(result).Single(s => s.Statement is RepeatStmt);
        Assert.False(repeat.Ok, "A non-integer REPEAT count must fail.");
        Assert.Contains(repeat.Diagnostics, d => d.Kind == "state-invalid-bound");
    }

    [Fact]
    public async Task Repeat_NegativeLiteralCount_IsInvalidBound()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session);
        var ast = Parse("REPEAT -2\n  EDIT\nEND");
        var result = await interp.RunAsync(ast);
        var repeat = Statements(result).Single(s => s.Statement is RepeatStmt);
        Assert.False(repeat.Ok);
        Assert.Contains(repeat.Diagnostics, d => d.Kind == "state-invalid-bound");
    }

    [Fact]
    public async Task Repeat_Zero_RunsBodyZeroTimes_AndIsNotAnError()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session);
        // REPEAT 0 with a body that WOULD touch the server (EDIT) — but the body never runs, so the
        // unreachable host is never contacted and the run succeeds with zero body-statement results.
        var ast = Parse("REPEAT 0\n  EDIT\nEND");
        var result = await interp.RunAsync(ast);

        // No EDIT statement-instance was emitted: the body ran zero times.
        Assert.DoesNotContain(Statements(result), s => s.Statement is EditStmt);
        // And REPEAT 0 is not itself a failure.
        Assert.DoesNotContain(Statements(result), s => !s.Ok);
    }

    [Fact]
    public async Task Repeat_Zero_LeavesIndexVarUnbound_AfterLoop()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session);
        // The loop never iterates, so @i is never bound; the loop-scoped binding is removed on exit.
        var ast = Parse("REPEAT 0 AS @i\nEND");
        var result = await interp.RunAsync(ast);
        Assert.True(result.Ok);
        Assert.False(interp.Variables.ContainsKey("i"), "REPEAT 0 must not leave the index var bound.");
    }

    [Fact]
    public async Task Repeat_FractionalCount_IsInvalidBound_NotTruncated()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session);
        // 2.9 must NOT silently truncate to 2 and run the body twice — a fraction can't be a bound.
        var ast = Parse("@n = 2.9\nREPEAT {{n}}\n  EDIT\nEND");
        var result = await interp.RunAsync(ast);
        var repeat = Statements(result).Single(s => s.Statement is RepeatStmt);
        Assert.False(repeat.Ok, "A fractional REPEAT count must fail, not truncate.");
        Assert.Contains(repeat.Diagnostics, d => d.Kind == "state-invalid-bound");
        Assert.DoesNotContain(Statements(result), s => s.Statement is EditStmt);
    }

    [Fact]
    public async Task Repeat_OverflowingCount_IsInvalidBound_NotWrapped()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session);
        // 2^32+1 must NOT wrap through (int) to 1 and run once — it's out of int range.
        var ast = Parse("@n = 4294967297\nREPEAT {{n}}\n  EDIT\nEND");
        var result = await interp.RunAsync(ast);
        var repeat = Statements(result).Single(s => s.Statement is RepeatStmt);
        Assert.False(repeat.Ok, "An out-of-int-range REPEAT count must fail, not wrap.");
        Assert.Contains(repeat.Diagnostics, d => d.Kind == "state-invalid-bound");
        Assert.DoesNotContain(Statements(result), s => s.Statement is EditStmt);
    }

    // --- REPEAT AST shape (parse only) --------------------------------------------------------

    [Fact]
    public void Repeat_ParsesToExpectedShape()
    {
        var ast = Parse("REPEAT 5 AS @i\n  EDIT\nEND");
        var r = ast.Steps.SelectMany(s => s.Statements).OfType<RepeatStmt>().Single();
        Assert.Equal("i", r.IndexVar);
        Assert.NotNull(r.Count);
        Assert.Single(r.Body);
        Assert.IsType<EditStmt>(r.Body[0]);
    }

    [Fact]
    public void ForEach_ParsesToExpectedShape()
    {
        var ast = Parse("FOR-EACH ROW Detail \"OrderLines\" WHERE Status = \"Inactive\" AS @row\n  EDIT\nEND");
        var fe = ast.Steps.SelectMany(s => s.Statements).OfType<ForEachRowStmt>().Single();
        Assert.Equal("OrderLines", fe.DetailName);
        Assert.Equal("Status", fe.MatchColumn);
        Assert.Equal(ExpectOp.Eq, fe.MatchOp);
        Assert.NotNull(fe.MatchValue);
        Assert.Equal("row", fe.RowVar);
        Assert.Single(fe.Body);
    }
}
