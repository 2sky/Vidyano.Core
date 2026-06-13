using Vidyano.Script.Diagnostics;
using Vidyano.Script.Runtime;
using Xunit;
using static Vidyano.Script.Tests.SuiteTestData;

namespace Vidyano.Script.Tests;

/// <summary>Covers <see cref="SuiteRunner.Classify"/> — the pure map from a finished <see cref="ScriptResult"/>
/// to a <see cref="FileOutcome"/>. The connection-before-parse ordering and the "skipped only when every
/// statement skipped" rule are the load-bearing cases.</summary>
public sealed class SuiteClassifyTests
{
    [Fact]
    public void Passing_IsPassed() =>
        Assert.Equal(FileOutcome.Passed, SuiteRunner.Classify(Passing()));

    [Fact]
    public void AssertionFailure_IsFailed() =>
        Assert.Equal(FileOutcome.Failed, SuiteRunner.Classify(Failing()));

    [Fact]
    public void EveryStatementSkipped_IsSkipped() =>
        Assert.Equal(FileOutcome.Skipped, SuiteRunner.Classify(AllSkipped()));

    [Fact]
    public void ParseDiagnostics_IsParse() =>
        Assert.Equal(FileOutcome.Parse, SuiteRunner.Classify(ParseError()));

    [Fact]
    public void NoBaseUri_IsConnection_NotParse() =>
        // StateNotConnected lands in ParseDiagnostics; it must classify as Connection (exit 3), not Parse (2).
        Assert.Equal(FileOutcome.Connection, SuiteRunner.Classify(NoUri()));

    [Fact]
    public void TransportFailure_IsConnection() =>
        Assert.Equal(FileOutcome.Connection, SuiteRunner.Classify(TransportFailMidRun()));

    [Fact]
    public void ResolveError_IsFailed_NotConnection() =>
        // A script that references a missing attribute is a failing test, not an infra problem.
        Assert.Equal(FileOutcome.Failed, SuiteRunner.Classify(Failing(kind: ErrorKind.ResolveAttribute)));
}
