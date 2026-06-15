using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidyano.Script.Runtime;

/// <summary>A discovered .visc unit to run: a display <paramref name="Name"/> (the source path shown in
/// diagnostics and reports) and the script <paramref name="Body"/>. Decoupled from the file system so a
/// suite runs from in-memory sources (tests) exactly as it does from disk.</summary>
public readonly record struct ViscSource(string Name, string Body);

/// <summary>How a single file in a suite ended. Exactly one applies per file; the aggregate process exit
/// code is a pure function of the set (see <see cref="SuiteExit.CodeFor"/>).</summary>
public enum FileOutcome
{
    /// <summary>Ran clean — every executed statement passed.</summary>
    Passed,
    /// <summary>Ran, but at least one assertion or guard check failed.</summary>
    Failed,
    /// <summary>Ran, but every statement was skipped by an unmet <c>REQUIRES</c> (nothing was asserted).</summary>
    Skipped,
    /// <summary>The per-file timeout fired before the script finished.</summary>
    Timeout,
    /// <summary>Never got going against the backend — no base URI, or a transport/connect failure.</summary>
    Connection,
    /// <summary>The source did not parse.</summary>
    Parse,
}

/// <summary>Outcome of running one file in a suite. <see cref="Script"/> is null when the file never produced
/// a <see cref="ScriptResult"/> (a timeout, or a transport fault thrown instead of a result); <see cref="Error"/>
/// then carries the reason. <see cref="Duration"/> is host wall-clock for the whole file (includes connect /
/// sign-in), distinct from <see cref="ScriptResult.Duration"/> which covers only statement execution.</summary>
public sealed record FileResult(
    string Source,
    FileOutcome Outcome,
    ScriptResult? Script,
    TimeSpan Duration,
    string? Error = null);

/// <summary>Aggregate of every <see cref="FileResult"/> in a suite run, in discovery order.</summary>
public sealed record SuiteResult(IReadOnlyList<FileResult> Files, TimeSpan Duration)
{
    /// <summary>True iff every file passed or was skipped — nothing failed, timed out, or failed to run.</summary>
    public bool Ok => Files.All(f => f.Outcome is FileOutcome.Passed or FileOutcome.Skipped);
}

/// <summary>
/// Derives the process exit code from a finished suite. Pure and total — no I/O, no clock — so the exit
/// contract (the one thing CI bets the build on) is unit-testable in isolation, the reason the runner is
/// shaped around a value model rather than side effects.
/// </summary>
/// <remarks>
/// Precedence, most-blocking first: any <see cref="FileOutcome.Connection"/> ⇒ <c>3</c> (the run couldn't
/// trust the backend, so even green files mean nothing); else any <see cref="FileOutcome.Parse"/> ⇒ <c>2</c>
/// (malformed input); else any <see cref="FileOutcome.Failed"/> or <see cref="FileOutcome.Timeout"/> ⇒
/// <c>1</c> (it ran and something went wrong — a timeout is a failure of that test, not the documented
/// "before any work happened" connection case, so it folds into <c>1</c>, not <c>3</c>); else <c>0</c>.
/// The codes match the single-file contract in <c>Cli</c> (0 ok / 1 fail / 2 parse / 3 connect).
/// </remarks>
public static class SuiteExit
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int Parse = 2;
    public const int Connection = 3;

    public static int CodeFor(SuiteResult suite)
    {
        var anyParse = false;
        var anyFailish = false;
        foreach (var f in suite.Files)
        {
            switch (f.Outcome)
            {
                // Most-blocking — short-circuit. Any Connection anywhere outranks Parse/Failed/Timeout.
                case FileOutcome.Connection: return Connection;
                case FileOutcome.Parse:      anyParse = true; break;
                case FileOutcome.Failed:
                case FileOutcome.Timeout:    anyFailish = true; break;
            }
        }
        if (anyParse) return Parse;
        if (anyFailish) return Failed;
        return Ok;
    }
}
