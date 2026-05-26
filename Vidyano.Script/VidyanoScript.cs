using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Parsing;
using Vidyano.Script.Runtime;

namespace Vidyano.Script;

/// <summary>
/// Public façade for running .visc scripts. The CLI, the (future) MCP server, and library callers
/// all go through here, so the surface stays small and the wiring stays internal.
/// </summary>
public static class VidyanoScript
{
    /// <summary>Parses and executes a .visc file from disk.</summary>
    public static Task<ScriptResult> RunFileAsync(string path, VidyanoScriptOptions? options = null)
    {
        if (!File.Exists(path))
            return Task.FromResult(MakeParseOnlyResult(path, new Diagnostic(
                ErrorKind.TransportError,
                $"File not found: {path}",
                new SourceLocation(path, 0, 0))));
        var source = File.ReadAllText(path);
        var opts = options ?? new VidyanoScriptOptions();
        opts.SourcePath = path;
        return RunAsync(source, opts);
    }

    /// <summary>Parses and executes a .visc body. <paramref name="options"/> can override the source path.</summary>
    public static async Task<ScriptResult> RunAsync(string body, VidyanoScriptOptions? options = null)
    {
        var opts = options ?? new VidyanoScriptOptions();
        var lexer = new Lexer(body, opts.SourcePath);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens, lexer.Diagnostics);
        var script = parser.Parse();
        var parseDiags = parser.Diagnostics;

        // If parsing produced any errors, surface them in a parse-only ScriptResult — we don't run a
        // partially-parsed script because the AST will be misleading and the first statement that
        // tries to touch the network adds noise to what's really a syntax problem.
        if (parseDiags.Any())
            return new ScriptResult(opts.SourcePath, false, Array.Empty<StepResult>(), parseDiags);

        var baseUri = opts.RemoteUri ?? PeekAppVar(script);
        if (string.IsNullOrEmpty(baseUri))
            return MakeParseOnlyResult(opts.SourcePath, new Diagnostic(
                ErrorKind.StateNotConnected,
                "No base URI for the Vidyano service.",
                script.Location,
                Hint: "Set @app = http://... in the script, or pass --app on the command line."));

        using var session = new VidyanoSession(baseUri!, opts.HttpClient, opts.AcceptAnyServerCertificate);
        var interpreter = new Interpreter(session, opts.Variables, opts.Mode);
        return await interpreter.RunAsync(script).ConfigureAwait(false);
    }

    /// <summary>Lints only — returns parse diagnostics without executing.</summary>
    public static IReadOnlyList<Diagnostic> Lint(string body, string sourcePath = "<inline>")
    {
        var lexer = new Lexer(body, sourcePath);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens, lexer.Diagnostics);
        _ = parser.Parse();
        return parser.Diagnostics;
    }

    private static string? PeekAppVar(Parsing.ScriptAst script)
    {
        // Scripts conventionally start with `@app = ...`. Look for it in the first step's preamble
        // so we can construct the Client before walking statements.
        foreach (var step in script.Steps)
            foreach (var stmt in step.Statements)
                if (stmt is VariableAssignment va &&
                    string.Equals(va.Name, "app", StringComparison.OrdinalIgnoreCase) &&
                    va.Value is LiteralExpr lit && lit.Value is string s)
                    return s;
        return null;
    }

    private static ScriptResult MakeParseOnlyResult(string path, Diagnostic d) =>
        new(path, false, Array.Empty<StepResult>(), new[] { d });
}
