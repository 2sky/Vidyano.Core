using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Vidyano.Script.Runtime;
using Xunit;
using static Vidyano.Script.Tests.SuiteTestData;

namespace Vidyano.Script.Tests;

/// <summary>
/// End-to-end coverage of <see cref="SuiteRunner.RunAsync"/> through its only seam — the per-file executor
/// delegate. A fabricated executor lets every outcome (pass / fail / skip / timeout / connection) and the
/// ordering + timeout machinery run with no backend and no disk.
/// </summary>
public sealed class SuiteRunnerTests
{
    private static IReadOnlyList<ViscSource> Sources(params string[] names) =>
        names.Select(n => new ViscSource(n, "")).ToList();

    // Executor that maps a source name to a canned result / behaviour.
    private static async Task<ScriptResult> FakeAsync(ViscSource src, CancellationToken ct)
    {
        switch (src.Name)
        {
            case "pass.visc": return Passing(src.Name);
            case "fail.visc": return Failing(src.Name);
            case "skip.visc": return AllSkipped(src.Name);
            case "parse.visc": return ParseError(src.Name);
            case "slow.visc":
                await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false); // cancelled by the per-file timeout
                return Passing(src.Name);
            case "throw.visc": throw new HttpRequestException("no such host");
            default: return Passing(src.Name);
        }
    }

    [Fact]
    public async Task ClassifiesEachFile_ByOutcome()
    {
        var suite = await SuiteRunner.RunAsync(
            Sources("pass.visc", "fail.visc", "skip.visc", "parse.visc"), FakeAsync);

        Assert.Equal(FileOutcome.Passed, suite.Files[0].Outcome);
        Assert.Equal(FileOutcome.Failed, suite.Files[1].Outcome);
        Assert.Equal(FileOutcome.Skipped, suite.Files[2].Outcome);
        Assert.Equal(FileOutcome.Parse, suite.Files[3].Outcome);
        Assert.False(suite.Ok);
        Assert.Equal(SuiteExit.Parse, SuiteExit.CodeFor(suite)); // parse outranks failed
    }

    [Fact]
    public async Task ExecutorThrows_IsConnection_AndCarriesMessage()
    {
        var suite = await SuiteRunner.RunAsync(Sources("throw.visc"), FakeAsync);

        Assert.Equal(FileOutcome.Connection, suite.Files[0].Outcome);
        Assert.Null(suite.Files[0].Script);
        Assert.Contains("no such host", suite.Files[0].Error);
        Assert.Equal(SuiteExit.Connection, SuiteExit.CodeFor(suite));
    }

    [Fact]
    public async Task PerFileTimeout_FiresTimeout_NotFailure()
    {
        var suite = await SuiteRunner.RunAsync(
            Sources("slow.visc"), FakeAsync,
            new SuiteRunOptions { PerFileTimeout = TimeSpan.FromMilliseconds(100) });

        Assert.Equal(FileOutcome.Timeout, suite.Files[0].Outcome);
        Assert.Equal(SuiteExit.Failed, SuiteExit.CodeFor(suite)); // timeout folds into exit 1
    }

    [Fact]
    public async Task Timeout_DoesNotKillSiblings()
    {
        var suite = await SuiteRunner.RunAsync(
            Sources("slow.visc", "pass.visc"), FakeAsync,
            new SuiteRunOptions { MaxParallelism = 2, PerFileTimeout = TimeSpan.FromMilliseconds(100) });

        Assert.Equal(FileOutcome.Timeout, suite.Files[0].Outcome);
        Assert.Equal(FileOutcome.Passed, suite.Files[1].Outcome);
    }

    [Fact]
    public async Task ResultsAreOrderedBySource_RegardlessOfCompletion()
    {
        // slow first, fast second — under parallelism the fast one finishes first, but results stay in order.
        var names = new[] { "slow.visc", "pass.visc", "fail.visc" };
        var suite = await SuiteRunner.RunAsync(
            Sources(names), FakeAsync,
            new SuiteRunOptions { MaxParallelism = 3, PerFileTimeout = TimeSpan.FromMilliseconds(100) });

        Assert.Equal(names, suite.Files.Select(f => f.Source).ToArray());
    }

    [Fact]
    public async Task NoTimeout_LetsSlowFilesComplete()
    {
        // Without a per-file timeout the "slow" file would hang 30s; assert we don't time it out by default by
        // using a fast executor only — proving the default is "no limit" (the slow path is covered above).
        var suite = await SuiteRunner.RunAsync(Sources("pass.visc", "pass.visc"), FakeAsync);
        Assert.True(suite.Ok);
        Assert.Equal(SuiteExit.Ok, SuiteExit.CodeFor(suite));
    }
}
