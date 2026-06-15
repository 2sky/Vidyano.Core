using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vidyano.Script.Diagnostics;

namespace Vidyano.Script.Runtime;

/// <summary>Knobs for a suite run.</summary>
public sealed record SuiteRunOptions
{
    /// <summary>How many files run concurrently. Clamped to ≥ 1. Default 1 (serial) — safe for suites whose
    /// scripts share server fixtures; the CLI raises it.</summary>
    public int MaxParallelism { get; init; } = 1;

    /// <summary>Per-file wall-clock budget. A file that exceeds it is cancelled and recorded as
    /// <see cref="FileOutcome.Timeout"/>. Null (or non-positive) means no per-file limit.</summary>
    public TimeSpan? PerFileTimeout { get; init; }
}

/// <summary>
/// Runs a set of <see cref="ViscSource"/>s through a per-file <paramref name="executor"/> and aggregates the
/// outcomes into a <see cref="SuiteResult"/>. The executor is the only seam that touches a backend — tests
/// inject a fake (canned results / delays / throws); production passes <see cref="VidyanoScript.RunSuiteAsync"/>'s
/// real run. Because the runner depends on nothing but a source list and a delegate, its whole behavior —
/// classification, timeout, parallelism, ordering — is exercisable in memory with no disk and no network.
/// </summary>
public static class SuiteRunner
{
    public static async Task<SuiteResult> RunAsync(
        IReadOnlyList<ViscSource> sources,
        Func<ViscSource, CancellationToken, Task<ScriptResult>> executor,
        SuiteRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SuiteRunOptions();
        var degree = Math.Max(1, options.MaxParallelism);
        var results = new FileResult[sources.Count];
        var suiteStart = Stopwatch.GetTimestamp();

        using var gate = new SemaphoreSlim(degree);
        var tasks = new Task[sources.Count];
        for (var i = 0; i < sources.Count; i++)
        {
            var index = i;
            var source = sources[i];
            tasks[i] = Task.Run(async () =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    results[index] = await RunOneAsync(source, executor, options.PerFileTimeout, cancellationToken)
                        .ConfigureAwait(false);
                }
                finally { gate.Release(); }
            }, cancellationToken);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return new SuiteResult(results, Stopwatch.GetElapsedTime(suiteStart));
    }

    private static async Task<FileResult> RunOneAsync(
        ViscSource source,
        Func<ViscSource, CancellationToken, Task<ScriptResult>> executor,
        TimeSpan? timeout,
        CancellationToken outer)
    {
        var start = Stopwatch.GetTimestamp();
        using var perFile = CancellationTokenSource.CreateLinkedTokenSource(outer);
        if (timeout is { } t && t > TimeSpan.Zero)
            perFile.CancelAfter(t);
        try
        {
            var result = await executor(source, perFile.Token).ConfigureAwait(false);
            return new FileResult(source.Name, Classify(result), result, Stopwatch.GetElapsedTime(start));
        }
        catch (OperationCanceledException) when (perFile.IsCancellationRequested && !outer.IsCancellationRequested)
        {
            // Our timeout fired (not a host-driven abort): a Timeout outcome, distinct from an assertion fail.
            return new FileResult(source.Name, FileOutcome.Timeout, null, Stopwatch.GetElapsedTime(start),
                timeout is { } to ? $"timed out after {to.TotalSeconds:0.#}s" : "timed out");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The executor faulted before producing a result — almost always a connect/transport failure
            // (bad host, TLS, DNS). Record Connection so the exit code reflects "couldn't run", not "failed".
            return new FileResult(source.Name, FileOutcome.Connection, null, Stopwatch.GetElapsedTime(start), ex.Message);
        }
        // An OperationCanceledException from the outer (host) token escapes RunAsync and aborts the suite.
    }

    /// <summary>Maps a completed <see cref="ScriptResult"/> to a <see cref="FileOutcome"/>. Pure — depends only
    /// on the result's diagnostics and pass/skip flags — so it is unit-testable from fabricated results.</summary>
    /// <remarks>
    /// A connect/transport diagnostic wins first, wherever it sits: the no-base-URI case lands in
    /// <see cref="ScriptResult.ParseDiagnostics"/> with <see cref="ErrorKind.StateNotConnected"/>, while a
    /// sign-in transport fault lands in a statement's diagnostics — both mean the file never ran against the
    /// backend, so both are <see cref="FileOutcome.Connection"/> and must be classified before Parse so the
    /// no-URI sentinel isn't mistaken for malformed syntax. (Timeout is decided by the runner, not here.)
    /// </remarks>
    public static FileOutcome Classify(ScriptResult result)
    {
        if (!result.Ok && AllDiagnostics(result).Any(d => IsConnect(d.Kind)))
            return FileOutcome.Connection;
        if (result.ParseDiagnostics.Count > 0)
            return FileOutcome.Parse;
        if (!result.Ok)
            return FileOutcome.Failed;

        var statements = result.Steps.SelectMany(s => s.Statements).ToList();
        if (statements.Count > 0 && statements.All(s => s.Skipped))
            return FileOutcome.Skipped;
        return FileOutcome.Passed;
    }

    private static IEnumerable<Diagnostic> AllDiagnostics(ScriptResult r) =>
        r.ParseDiagnostics.Concat(r.Steps.SelectMany(s => s.Statements).SelectMany(s => s.Diagnostics));

    // A mid-run transport blip folds into Connection too — for CI that's the honest "retry the job" signal,
    // not "your assertion is wrong". A ServerError (the server rejecting a real operation) stays a Failed.
    private static bool IsConnect(string kind) =>
        kind is ErrorKind.StateNotConnected or ErrorKind.TransportError;
}
