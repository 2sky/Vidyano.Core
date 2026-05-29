using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vidyano.Script.Parsing;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Runtime behavior of environment-variable value sourcing, exercised WITHOUT a live server by
/// injecting a fake <c>EnvLookup</c> into the interpreter. Covers the <c>{{env:NAME}}</c> hole
/// (loud-on-missing, empty == missing, <c>??</c> fallback), <c>SIGN-IN FROM ENV</c>'s loud-fail
/// (which short-circuits before any network call), and the <c>--env-prefix</c> bulk binding +
/// <c>--var</c> precedence. The prefix tests use the real process env because the bulk binder reads
/// it directly (documented on <c>EnvironmentPrefix</c>), with try/finally cleanup.
/// </summary>
public sealed class EnvSourcingTests
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

    /// <summary>A session pointed at an unreachable host. Safe: no network happens until a verb talks
    /// to the server, and these tests deliberately avoid those (SIGN-IN FROM ENV loud-fails first).</summary>
    private static VidyanoSession NewSession() =>
        new("https://127.0.0.1:1", acceptAnyServerCertificate: true);

    private static Interpreter NewInterpreter(
        VidyanoSession session,
        IDictionary<string, string?> env,
        IReadOnlyDictionary<string, object?>? initialVars = null,
        string? envPrefix = null) =>
        new(session, initialVars: initialVars, mode: GuardMode.Navigation, tools: null,
            cancellationToken: default, now: null, seed: null,
            envLookup: name => env.TryGetValue(name, out var v) ? v : null,
            envPrefix: envPrefix);

    private static List<StatementResult> Statements(ScriptResult result) =>
        result.Steps.SelectMany(s => s.Statements).ToList();

    // --- {{env:NAME}} resolution --------------------------------------------------------------

    [Fact]
    public async Task EnvHole_Set_ResolvesToValue()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session, new Dictionary<string, string?> { ["MY_VAR"] = "hello" });
        var result = await interp.RunAsync(Parse("EXPECT {{env:MY_VAR}} = \"hello\""));
        Assert.True(Statements(result).Single().Ok, "{{env:MY_VAR}} should resolve to its value.");
    }

    [Fact]
    public async Task EnvHole_Missing_LoudFails()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session, new Dictionary<string, string?>());
        var result = await interp.RunAsync(Parse("@x = {{env:MY_VAR}}"));
        var stmt = Statements(result).Single();
        Assert.False(stmt.Ok, "A missing {{env:NAME}} must fail loudly, not resolve to empty.");
        Assert.Contains(stmt.Diagnostics, d => d.Kind == "resolve-env");
    }

    [Fact]
    public async Task EnvHole_EmptyValue_TreatedAsMissing()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session, new Dictionary<string, string?> { ["MY_VAR"] = "" });
        var result = await interp.RunAsync(Parse("@x = {{env:MY_VAR}}"));
        var stmt = Statements(result).Single();
        Assert.False(stmt.Ok, "An empty env var must be treated as missing (closes the empty-credential footgun).");
        Assert.Contains(stmt.Diagnostics, d => d.Kind == "resolve-env");
    }

    [Fact]
    public async Task EnvHole_Missing_UsesFallback()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session, new Dictionary<string, string?>());
        var result = await interp.RunAsync(Parse("@x = {{env:MY_VAR ?? \"fallback\"}}\nEXPECT {{x}} = \"fallback\""));
        Assert.True(Statements(result).Last().Ok, "A missing env var with a ?? fallback should use the fallback.");
    }

    [Fact]
    public async Task EnvHole_EmptyValue_UsesFallback()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session, new Dictionary<string, string?> { ["MY_VAR"] = "" });
        var result = await interp.RunAsync(Parse("@x = {{env:MY_VAR ?? \"fallback\"}}\nEXPECT {{x}} = \"fallback\""));
        Assert.True(Statements(result).Last().Ok, "An empty env var should fall through to the ?? fallback.");
    }

    [Fact]
    public async Task EnvHole_Set_BeatsFallback()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session, new Dictionary<string, string?> { ["MY_VAR"] = "real" });
        var result = await interp.RunAsync(Parse("@x = {{env:MY_VAR ?? \"fallback\"}}\nEXPECT {{x}} = \"real\""));
        Assert.True(Statements(result).Last().Ok, "A set env var should win over its ?? fallback.");
    }

    // --- SIGN-IN FROM ENV loud-fail (short-circuits before any network) -----------------------

    [Fact]
    public async Task SignInFromEnv_MissingUser_LoudFailsWithoutNetwork()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session, new Dictionary<string, string?> { ["VIDYANO_PASSWORD"] = "pw" });
        var result = await interp.RunAsync(Parse("SIGN-IN FROM ENV"));
        var stmt = Statements(result).Single();
        Assert.False(stmt.Ok, "SIGN-IN FROM ENV must loud-fail when VIDYANO_USER is unset.");
        Assert.Contains(stmt.Diagnostics, d => d.Kind == "resolve-env");
    }

    [Fact]
    public async Task SignInFromEnv_EmptyPassword_LoudFails()
    {
        using var session = NewSession();
        var interp = NewInterpreter(session,
            new Dictionary<string, string?> { ["VIDYANO_USER"] = "admin", ["VIDYANO_PASSWORD"] = "" });
        var result = await interp.RunAsync(Parse("SIGN-IN FROM ENV"));
        var stmt = Statements(result).Single();
        Assert.False(stmt.Ok, "An empty VIDYANO_PASSWORD must be treated as missing.");
        Assert.Contains(stmt.Diagnostics, d => d.Kind == "resolve-env");
    }

    // --- --env-prefix bulk binding + --var precedence -----------------------------------------

    [Fact]
    public async Task EnvPrefix_BindsStrippedVar()
    {
        const string key = "VIDYTEST_REGION";
        Environment.SetEnvironmentVariable(key, "eu");
        try
        {
            using var session = NewSession();
            // The bulk binder reads the live process env (not the injected lookup), so set a real var.
            var interp = NewInterpreter(session, new Dictionary<string, string?>(), envPrefix: "VIDYTEST_");
            var result = await interp.RunAsync(Parse("EXPECT {{REGION}} = \"eu\""));
            Assert.True(Statements(result).Single().Ok, "--env-prefix should bind VIDYTEST_REGION as {{REGION}}.");
        }
        finally { Environment.SetEnvironmentVariable(key, null); }
    }

    [Fact]
    public async Task EnvPrefix_ExplicitVarWins()
    {
        // Distinct key/prefix from EnvPrefix_BindsStrippedVar so the two tests can never clobber each
        // other's process-wide env state, regardless of test-runner parallelism.
        const string key = "VIDYTEST_EXPLICIT_REGION";
        Environment.SetEnvironmentVariable(key, "eu");
        try
        {
            using var session = NewSession();
            var initial = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["REGION"] = "explicit" };
            var interp = NewInterpreter(session, new Dictionary<string, string?>(), initialVars: initial, envPrefix: "VIDYTEST_EXPLICIT_");
            var result = await interp.RunAsync(Parse("EXPECT {{REGION}} = \"explicit\""));
            Assert.True(Statements(result).Single().Ok, "An explicit --var/initialVars binding must win over an env-prefix bind.");
        }
        finally { Environment.SetEnvironmentVariable(key, null); }
    }
}
