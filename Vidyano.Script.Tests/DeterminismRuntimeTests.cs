using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Vidyano.Script.Parsing;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Interpreter behavior for the determinism feature set, exercised WITHOUT a live server. Every
/// script here uses only server-independent statements: built-in interpolation subjects, REQUIRES
/// gates that resolve from session/expression state, and CLEANUP. Anything needing CurrentPo /
/// sign-in is covered at the parse level in <see cref="LintTests"/> instead.
/// </summary>
public sealed class DeterminismRuntimeTests
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

    private static Interpreter NewInterpreter(
        VidyanoSession session,
        DateTimeOffset? now = null,
        int? seed = null,
        IReadOnlyDictionary<string, ScriptToolHandler>? tools = null) =>
        new(TestSessionBook.Wrap(session), initialVars: null, mode: GuardMode.Navigation, tools: tools, cancellationToken: default,
            now: now, seed: seed);

    private static List<StatementResult> Statements(ScriptResult result) =>
        result.Steps.SelectMany(s => s.Statements).ToList();

    // --- OPEN-ROW WHERE AST shape (parse only; runtime needs a live Query) --------------------

    [Fact]
    public void OpenRowWhere_ParsesToByValueShape()
    {
        var ast = Parse("OPEN-ROW WHERE Name = \"Acme\" AS @acme");
        var or = ast.Steps.SelectMany(s => s.Statements).OfType<OpenRowStmt>().Single();
        Assert.Null(or.Index);
        Assert.Equal("Name", or.MatchColumn);
        Assert.Equal(ExpectOp.Eq, or.MatchOp);
        Assert.NotNull(or.MatchValue);
        Assert.Equal("acme", or.AsHandle);
    }

    [Fact]
    public void OpenRowPositional_ParsesToIndexShape()
    {
        var ast = Parse("OPEN-ROW 0");
        var or = ast.Steps.SelectMany(s => s.Statements).OfType<OpenRowStmt>().Single();
        Assert.NotNull(or.Index);
        Assert.Null(or.MatchColumn);
        Assert.Null(or.MatchOp);
        Assert.Null(or.MatchValue);
    }

    // --- built-in var determinism -------------------------------------------------------------

    [Fact]
    public async Task PinnedNow_Today_FormatsAsIsoDate()
    {
        var now = new DateTimeOffset(2026, 5, 26, 9, 0, 0, TimeSpan.Zero);
        using var session = NewSession();
        var interp = NewInterpreter(session, now: now);
        // MATCHES on the literal pinned date proves @today == Now formatted yyyy-MM-dd.
        var ast = Parse("EXPECT {{@today}} = \"2026-05-26\"");
        var result = await interp.RunAsync(ast);
        Assert.True(Statements(result).Single().Ok, "EXPECT {{@today}} should equal the pinned date.");
    }

    [Fact]
    public async Task SameSeed_ReproducesUuidRandomAndTodayAcrossRuns()
    {
        var now = new DateTimeOffset(2026, 5, 26, 9, 0, 0, TimeSpan.Zero);
        const int seed = 42;

        var (uuidA, randomA, todayA, nowA) = await CaptureBuiltins(now, seed);
        var (uuidB, randomB, todayB, nowB) = await CaptureBuiltins(now, seed);

        // The seeded streams replay identically: the first draw of each run matches.
        Assert.Equal(uuidA, uuidB);
        Assert.Equal(randomA, randomB);
        // @today is derived from the pinned anchor, so it is stable too.
        Assert.Equal(todayA, todayB);
        // @now flows from the anchor by real elapsed time, so it is anchored but NOT bit-reproducible.
        Assert.StartsWith("2026-05-26T09:00:00", nowA);
        Assert.StartsWith("2026-05-26T09:00:00", nowB);
    }

    [Fact]
    public async Task DifferentSeeds_ProduceDifferentUuidAndRandom()
    {
        var now = new DateTimeOffset(2026, 5, 26, 9, 0, 0, TimeSpan.Zero);
        var (uuidA, randomA, _, _) = await CaptureBuiltins(now, seed: 1);
        var (uuidB, randomB, _, _) = await CaptureBuiltins(now, seed: 2);

        Assert.NotEqual(uuidA, uuidB);
        Assert.NotEqual(randomA, randomB);
    }

    [Fact]
    public async Task EachUuidReference_DrawsADistinctValue()
    {
        var now = new DateTimeOffset(2026, 5, 26, 9, 0, 0, TimeSpan.Zero);
        using var session = NewSession();
        var interp = NewInterpreter(session, now: now, seed: 7);
        // Built-ins evaluate per reference, so two bare {{@uuid}} draws are the next two values in the
        // stream — distinct. (The @-prefix is reserved for built-ins/scopes; a user var is {{name}}.)
        var ast = Parse("@a = {{@uuid}}\n@b = {{@uuid}}\nEXPECT {{a}} != {{b}}");
        var result = await interp.RunAsync(ast);
        Assert.True(Statements(result).Last().Ok, "Consecutive {{@uuid}} references must produce distinct ids.");
    }

    [Fact]
    public async Task CapturingABuiltin_FreezesItForReuse()
    {
        var now = new DateTimeOffset(2026, 5, 26, 9, 0, 0, TimeSpan.Zero);
        using var session = NewSession();
        var interp = NewInterpreter(session, now: now, seed: 7);
        // Capture once, then read the variable twice: the captured value is stable, mirroring the
        // C# `var id = uuid()` idiom the language documents as the way to freeze a built-in.
        var ast = Parse("@id = {{@uuid}}\nEXPECT {{id}} = {{id}}");
        var result = await interp.RunAsync(ast);
        Assert.True(Statements(result).Last().Ok, "A captured built-in must be stable across reads.");
    }

    [Fact]
    public async Task UuidAndRandomStreams_AreIndependent()
    {
        var now = new DateTimeOffset(2026, 5, 26, 9, 0, 0, TimeSpan.Zero);
        const int seed = 99;

        // Run A: two uuid draws back to back.
        string a1, a2;
        {
            using var session = NewSession();
            var interp = NewInterpreter(session, now: now, seed: seed);
            await interp.RunAsync(Parse("@x = {{@uuid}}\n@y = {{@uuid}}"));
            a1 = Convert.ToString(interp.Variables["x"], CultureInfo.InvariantCulture)!;
            a2 = Convert.ToString(interp.Variables["y"], CultureInfo.InvariantCulture)!;
        }

        // Run B: a @random draw interleaved between the two uuid draws.
        string b1, b2;
        {
            using var session = NewSession();
            var interp = NewInterpreter(session, now: now, seed: seed);
            await interp.RunAsync(Parse("@x = {{@uuid}}\n@r = {{@random}}\n@y = {{@uuid}}"));
            b1 = Convert.ToString(interp.Variables["x"], CultureInfo.InvariantCulture)!;
            b2 = Convert.ToString(interp.Variables["y"], CultureInfo.InvariantCulture)!;
        }

        // The interleaved @random must not shift the uuid sequence: both uuid draws match across runs.
        Assert.Equal(a1, b1);
        Assert.Equal(a2, b2);
    }

    [Fact]
    public async Task PinnedUuid_MatchesGuidShape()
    {
        var now = new DateTimeOffset(2026, 5, 26, 9, 0, 0, TimeSpan.Zero);
        using var session = NewSession();
        var interp = NewInterpreter(session, now: now, seed: 42);
        var ast = Parse("EXPECT {{@uuid}} MATCHES \"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$\"");
        var result = await interp.RunAsync(ast);
        Assert.True(Statements(result).Single().Ok, "Seeded {{@uuid}} should be a canonical GUID string.");
    }

    /// <summary>Reads the four built-ins out of one run by stuffing each into a variable and reading
    /// it back from the interpreter's variable table.</summary>
    private static async Task<(string uuid, string random, string today, string now)> CaptureBuiltins(
        DateTimeOffset now, int seed)
    {
        using var session = NewSession();
        var interp = NewInterpreter(session, now: now, seed: seed);
        // Bare {{...}} so the built-in is evaluated and stored, not kept as literal string text.
        var ast = Parse(
            "@u = {{@uuid}}\n" +
            "@r = {{@random}}\n" +
            "@t = {{@today}}\n" +
            "@n = {{@now}}");
        await interp.RunAsync(ast);
        return (
            Convert.ToString(interp.Variables["u"], CultureInfo.InvariantCulture)!,
            Convert.ToString(interp.Variables["r"], CultureInfo.InvariantCulture)!,
            Convert.ToString(interp.Variables["t"], CultureInfo.InvariantCulture)!,
            Convert.ToString(interp.Variables["n"], CultureInfo.InvariantCulture)!);
    }

    // --- EXPECT ... MATCHES evaluation --------------------------------------------------------

    [Fact]
    public async Task Matches_PositiveAgainstInterpolation_Passes()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session);
        var ast = Parse("@code = \"AB123\"\nEXPECT {{code}} MATCHES \"^[A-Z]{2}\\\\d+$\"");
        var result = await interp.RunAsync(ast);
        Assert.True(Statements(result).Last().Ok, "MATCHES should pass for a matching subject.");
    }

    [Fact]
    public async Task Matches_NonMatch_Fails()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session);
        var ast = Parse("@code = \"zzz\"\nEXPECT {{code}} MATCHES \"^[A-Z]{2}\\\\d+$\"");
        var result = await interp.RunAsync(ast);
        var expectStmt = Statements(result).Last();
        Assert.False(expectStmt.Ok, "MATCHES should fail for a non-matching subject.");
    }

    [Fact]
    public async Task Matches_NullLeft_NeverMatches()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session);
        // {{@missing}} would be a resolution error; instead use an explicit null via a variable that
        // is set to null. A literal null subject through interpolation: assign null then MATCHES ".*".
        var ast = Parse("@x = null\nEXPECT {{x}} MATCHES \".*\"");
        var result = await interp.RunAsync(ast);
        var expectStmt = Statements(result).Last();
        Assert.False(expectStmt.Ok, "MATCHES against a null subject should never match, even with .*");
    }

    [Fact]
    public async Task Matches_InvalidPattern_FailsCleanlyWithoutCrashing()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session);
        // "[" is a valid string literal but a malformed regex. This must surface as a clean failed
        // assertion (parse-invalid-value), not let RegexParseException escape and abort the run.
        var ast = Parse("@code = \"abc\"\nEXPECT {{code}} MATCHES \"[\"");
        var result = await interp.RunAsync(ast);
        var expectStmt = Statements(result).Last();
        Assert.False(expectStmt.Ok, "A malformed MATCHES pattern should fail the assertion, not crash.");
        Assert.Contains(expectStmt.Diagnostics, d => d.Kind == "parse-invalid-value");
    }

    // --- REQUIRES skip propagation ------------------------------------------------------------

    [Fact]
    public async Task RequiresTool_Unregistered_SkipsRestOfBody()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session);
        var ast = Parse(
            "REQUIRES TOOL not-registered\n" +
            "@after = \"1\"\n" +
            "EXPECT {{after}} = \"1\"");
        var result = await interp.RunAsync(ast);
        var stmts = Statements(result);

        // The REQUIRES itself: skipped pass with the state-requires-unmet diagnostic.
        Assert.True(stmts[0].Ok);
        Assert.True(stmts[0].Skipped);
        Assert.Contains(stmts[0].Diagnostics, d => d.Kind == "state-requires-unmet");

        // Every subsequent body statement: skipped pass, no execution.
        Assert.True(stmts[1].Ok && stmts[1].Skipped);
        Assert.True(stmts[2].Ok && stmts[2].Skipped);
    }

    [Fact]
    public async Task RequiresDataGate_Unmet_SkipsRestOfBody()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session, seed: 5);
        // @random is a number that will never equal this string -> gate unmet -> skip.
        var ast = Parse(
            "REQUIRES {{@random}} = \"definitely-not-the-random-value\"\n" +
            "@after = \"1\"\n" +
            "EXPECT {{after}} = \"1\"");
        var result = await interp.RunAsync(ast);
        var stmts = Statements(result);

        Assert.True(stmts[0].Ok && stmts[0].Skipped);
        Assert.Contains(stmts[0].Diagnostics, d => d.Kind == "state-requires-unmet");
        Assert.True(stmts[1].Ok && stmts[1].Skipped);
        Assert.True(stmts[2].Ok && stmts[2].Skipped);
    }

    [Fact]
    public async Task RequiresDataGate_Met_ContinuesNormally()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session);
        var ast = Parse(
            "@v = \"ok\"\n" +
            "REQUIRES {{v}} = \"ok\"\n" +
            "EXPECT {{v}} = \"ok\"");
        var result = await interp.RunAsync(ast);
        var stmts = Statements(result);

        var requires = stmts.Single(s => s.Statement is RequiresStmt);
        Assert.True(requires.Ok);
        Assert.False(requires.Skipped);

        var expect = stmts.Last();
        Assert.True(expect.Ok && !expect.Skipped, "A met REQUIRES must not skip the body.");
    }

    // --- CLEANUP always runs ------------------------------------------------------------------

    [Fact]
    public async Task Cleanup_RunsEvenWhenBodySkipped()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session);
        var ast = Parse(
            "REQUIRES TOOL not-registered\n" +
            "@body = \"x\"\n" +
            "CLEANUP\n" +
            "@cleaned = \"y\"\n" +
            "EXPECT {{cleaned}} = \"y\"");
        var result = await interp.RunAsync(ast);
        var stmts = Statements(result);

        var bodyAssign = stmts.Single(s => s.Statement is VariableAssignment va && va.Name == "body");
        Assert.True(bodyAssign.Skipped, "Body statement before CLEANUP should be skipped.");

        // The cleanup EXPECT actually executed (not skipped) and passed.
        var cleanupExpect = stmts.Last();
        Assert.True(cleanupExpect.Ok, "Cleanup EXPECT should pass.");
        Assert.False(cleanupExpect.Skipped, "Statements after CLEANUP must run even when the body was skipped.");
    }

    [Fact]
    public async Task CleanupMarker_ItselfIsNormalPass()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session);
        var ast = Parse("REQUIRES TOOL nope\nCLEANUP");
        var result = await interp.RunAsync(ast);
        var marker = Statements(result).Single(s => s.Statement is CleanupMarker);
        Assert.True(marker.Ok);
        Assert.False(marker.Skipped);
    }

    // --- per-run state reset (D3): REPL reuses one Interpreter --------------------------------

    [Fact]
    public async Task SkipFlag_DoesNotLeakAcrossRuns()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session); // ONE interpreter, reused like the REPL does.

        // Run 1: an unmet REQUIRES sets the skip flag and skips the body.
        var run1 = await interp.RunAsync(Parse(
            "REQUIRES TOOL not-registered\n@a = \"1\"\nEXPECT {{a}} = \"1\""));
        Assert.True(Statements(run1)[1].Skipped, "Run 1 body should be skipped.");

        // Run 2: no REQUIRES at all. The skip flag must have been reset at the top of RunAsync.
        var run2 = await interp.RunAsync(Parse(
            "@b = \"2\"\nEXPECT {{b}} = \"2\""));
        var run2Stmts = Statements(run2);
        Assert.All(run2Stmts, s => Assert.False(s.Skipped,
            "Run 2 statements must NOT be skipped — skip flag leaked across RunAsync calls (D3 regression)."));
        Assert.True(run2Stmts.Last().Ok);
    }

    [Fact]
    public async Task BuiltinMemo_ResetsAcrossRuns_WhenUnseeded()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session); // unseeded: each run draws a fresh uuid.

        var run1 = await CaptureUuid(interp);
        var run2 = await CaptureUuid(interp);

        // If the uuid memo leaked across runs, these would be identical despite no seed.
        Assert.NotEqual(run1, run2);
    }

    private static async Task<string> CaptureUuid(Interpreter interp)
    {
        var result = await interp.RunAsync(Parse("@u = {{@uuid}}"));
        Assert.True(result.Ok);
        return Convert.ToString(interp.Variables["u"], CultureInfo.InvariantCulture)!;
    }
}
