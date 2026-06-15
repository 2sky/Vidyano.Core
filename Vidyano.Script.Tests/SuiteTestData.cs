using System;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Parsing;
using Vidyano.Script.Runtime;

namespace Vidyano.Script.Tests;

/// <summary>Fabricates <see cref="ScriptResult"/> / <see cref="FileResult"/> / <see cref="SuiteResult"/> values
/// from the public result types — no server, no disk — so the suite runner, classifier, exit-code function and
/// report formatters are all exercisable in memory.</summary>
internal static class SuiteTestData
{
    private static readonly SourceLocation Loc = new("<test>", 1, 1);
    private static Statement Stmt => new GoBackStmt(Loc);

    public static ScriptResult Passing(string name = "pass.visc")
    {
        var st = new StatementResult(Stmt, true, null, Array.Empty<Diagnostic>());
        var step = new StepResult("ok", Loc, true, new[] { st });
        return new ScriptResult(name, true, new[] { step }, Array.Empty<Diagnostic>());
    }

    public static ScriptResult Failing(string name = "fail.visc", string kind = ErrorKind.AssertFailed, string message = "boom")
    {
        var d = new Diagnostic(kind, message, Loc);
        var st = new StatementResult(Stmt, false, null, new[] { d });
        var step = new StepResult("bad", Loc, false, new[] { st });
        return new ScriptResult(name, false, new[] { step }, Array.Empty<Diagnostic>());
    }

    public static ScriptResult AllSkipped(string name = "skip.visc")
    {
        var d = new Diagnostic(ErrorKind.StateRequiresUnmet, "skipped", Loc);
        var st = new StatementResult(Stmt, true, null, new[] { d }, Skipped: true);
        var step = new StepResult("body", Loc, true, new[] { st }, Skipped: true);
        return new ScriptResult(name, true, new[] { step }, Array.Empty<Diagnostic>());
    }

    public static ScriptResult ParseError(string name = "parse.visc")
    {
        var d = new Diagnostic(ErrorKind.ParseUnexpectedToken, "unexpected token", Loc);
        return new ScriptResult(name, false, Array.Empty<StepResult>(), new[] { d });
    }

    /// <summary>The no-base-URI shape VidyanoScript returns: a parse-only result whose sole diagnostic is
    /// StateNotConnected — which used to be misclassified as a parse error (exit 2) instead of a connection
    /// error (exit 3).</summary>
    public static ScriptResult NoUri(string name = "connect.visc")
    {
        var d = new Diagnostic(ErrorKind.StateNotConnected, "no base uri", Loc);
        return new ScriptResult(name, false, Array.Empty<StepResult>(), new[] { d });
    }

    public static ScriptResult TransportFailMidRun(string name = "transport.visc")
    {
        var d = new Diagnostic(ErrorKind.TransportError, "connection reset", Loc);
        var st = new StatementResult(Stmt, false, null, new[] { d });
        var step = new StepResult("signin", Loc, false, new[] { st });
        return new ScriptResult(name, false, new[] { step }, Array.Empty<Diagnostic>());
    }

    public static FileResult File(string name, FileOutcome outcome) =>
        new(name, outcome, null, TimeSpan.Zero);

    /// <summary>Wraps a fabricated <see cref="ScriptResult"/> into a <see cref="FileResult"/> using the real
    /// classifier, so formatter tests see exactly what production would produce.</summary>
    public static FileResult FromScript(ScriptResult script) =>
        new(script.SourcePath, SuiteRunner.Classify(script), script, TimeSpan.Zero);

    public static SuiteResult Suite(params FileResult[] files) => new(files, TimeSpan.Zero);
}
