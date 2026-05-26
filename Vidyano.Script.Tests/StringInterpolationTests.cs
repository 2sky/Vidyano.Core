using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Vidyano.Script.Parsing;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// In-string interpolation: <c>{{...}}</c> holes inside a <c>"..."</c> literal resolve with the same
/// machinery as a standalone interpolation, so values compose (e.g. <c>"Acme {{@uuid}}"</c>). Exercised
/// without a live server via variable assignment + read-back, the same tactic as
/// <see cref="DeterminismRuntimeTests"/>.
/// </summary>
public sealed class StringInterpolationTests
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

    private static VidyanoSession NewSession() =>
        new("https://127.0.0.1:1", acceptAnyServerCertificate: true);

    private static Interpreter NewInterpreter(DateTimeOffset? now = null, int? seed = null) =>
        new(NewSession(), initialVars: null, mode: GuardMode.Navigation, tools: null, cancellationToken: default,
            now: now, seed: seed);

    private static string Var(Interpreter interp, string name) =>
        Convert.ToString(interp.Variables[name], CultureInfo.InvariantCulture)!;

    [Fact]
    public async Task Hole_ComposesWithSurroundingLiteral()
    {
        var interp = NewInterpreter(seed: 42);
        // "Acme {{@uuid}}" must become "Acme " + the seeded GUID.
        var result = await interp.RunAsync(Parse(
            "@name = \"Acme {{@uuid}}\"\n" +
            "EXPECT {{name}} MATCHES \"^Acme [0-9a-fA-F-]{36}$\""));
        Assert.True(result.Steps.SelectMany(s => s.Statements).Last().Ok,
            "An in-string {{@uuid}} should compose into the surrounding literal.");
    }

    [Fact]
    public async Task MultipleHolesAndLiterals_Compose()
    {
        var interp = NewInterpreter();
        var result = await interp.RunAsync(Parse(
            "@email = \"test+{{@uuid}}@example.com\"\n" +
            "EXPECT {{email}} MATCHES \"^test\\\\+[0-9a-fA-F-]{36}@example\\\\.com$\""));
        Assert.True(result.Steps.SelectMany(s => s.Statements).Last().Ok,
            "Literal + hole + literal should concatenate in order.");
    }

    [Fact]
    public async Task PinnedToday_ComposesIntoLabel()
    {
        var now = new DateTimeOffset(2026, 5, 26, 9, 0, 0, TimeSpan.Zero);
        var interp = NewInterpreter(now: now);
        var result = await interp.RunAsync(Parse(
            "@label = \"{{@today}}-report\"\n" +
            "EXPECT {{label}} = \"2026-05-26-report\""));
        Assert.True(result.Steps.SelectMany(s => s.Statements).Last().Ok);
    }

    [Fact]
    public async Task HoleFreeString_IsUnchanged()
    {
        var interp = NewInterpreter();
        var result = await interp.RunAsync(Parse(
            "@plain = \"just text\"\n" +
            "EXPECT {{plain}} = \"just text\""));
        Assert.True(result.Steps.SelectMany(s => s.Statements).Last().Ok,
            "A string without holes must keep its exact literal value.");
    }

    [Fact]
    public async Task InStringUuid_AdvancesTheSameStreamAsBare()
    {
        var interp = NewInterpreter();
        // Built-ins evaluate per reference through both paths, so the bare draw and the in-string
        // draw are two consecutive values from the one uuid stream — distinct, not equal.
        var result = await interp.RunAsync(Parse(
            "@bare = {{@uuid}}\n" +
            "@composed = \"x-{{@uuid}}\"\n" +
            "EXPECT {{composed}} NOT CONTAINS {{bare}}"));
        Assert.True(result.Steps.SelectMany(s => s.Statements).Last().Ok,
            "A bare and an in-string {{@uuid}} are separate draws, so they must differ.");
    }

    [Fact]
    public async Task CapturedUuid_ComposesStablyIntoAString()
    {
        var interp = NewInterpreter();
        // The idiom for reuse: capture once, then embed the captured variable in a string literal.
        var result = await interp.RunAsync(Parse(
            "@id = {{@uuid}}\n" +
            "@composed = \"x-{{id}}\"\n" +
            "EXPECT {{composed}} CONTAINS {{id}}"));
        Assert.True(result.Steps.SelectMany(s => s.Statements).Last().Ok,
            "A captured uuid must embed unchanged into an interpolated string.");
    }

    [Fact]
    public async Task SameSeed_ComposedValueIsDeterministicAcrossRuns()
    {
        var now = new DateTimeOffset(2026, 5, 26, 9, 0, 0, TimeSpan.Zero);
        var a = NewInterpreter(now: now, seed: 99);
        var b = NewInterpreter(now: now, seed: 99);
        await a.RunAsync(Parse("@n = \"row-{{@uuid}}\""));
        await b.RunAsync(Parse("@n = \"row-{{@uuid}}\""));
        Assert.Equal(Var(a, "n"), Var(b, "n"));
    }

    [Fact]
    public async Task EscapedBraces_StayLiteral()
    {
        var interp = NewInterpreter();
        // `\{` escapes a brace, so `a\{{b` is the literal text `a{{b` — no hole, no interpolation.
        var result = await interp.RunAsync(Parse(
            "@b = \"a\\{{b\"\n" +
            "EXPECT {{b}} = \"a\\{{b\""));
        Assert.True(result.Steps.SelectMany(s => s.Statements).Last().Ok,
            "An escaped \\{ must produce a literal brace, not open a hole.");
    }

    [Fact]
    public async Task UndefinedVariableInHole_FailsLoudly()
    {
        var interp = NewInterpreter();
        var result = await interp.RunAsync(Parse("@x = \"v-{{nope}}\""));
        var stmt = result.Steps.SelectMany(s => s.Statements).Single();
        Assert.False(stmt.Ok, "An undefined variable inside a string hole must fail the statement.");
        Assert.Contains(stmt.Diagnostics, d => d.Kind == "resolve-variable");
    }
}
