using Vidyano.Script.Runtime;
using Xunit;
using static Vidyano.Script.Tests.SuiteTestData;

namespace Vidyano.Script.Tests;

/// <summary>
/// The exit-code contract is the one thing CI bets the build on, so <see cref="SuiteExit.CodeFor"/> is a pure
/// function and gets exhaustive coverage here — every outcome and every precedence edge, in memory.
/// </summary>
public sealed class SuiteExitTests
{
    [Fact]
    public void AllPassed_Zero() =>
        Assert.Equal(0, SuiteExit.CodeFor(Suite(File("a", FileOutcome.Passed), File("b", FileOutcome.Passed))));

    [Fact]
    public void PassedAndSkipped_StillZero() =>
        Assert.Equal(0, SuiteExit.CodeFor(Suite(File("a", FileOutcome.Passed), File("b", FileOutcome.Skipped))));

    [Fact]
    public void Empty_Zero() =>
        Assert.Equal(0, SuiteExit.CodeFor(Suite()));

    [Fact]
    public void AnyFailed_One() =>
        Assert.Equal(1, SuiteExit.CodeFor(Suite(File("a", FileOutcome.Passed), File("b", FileOutcome.Failed))));

    [Fact]
    public void Timeout_FoldsIntoOne_NotThree() =>
        Assert.Equal(1, SuiteExit.CodeFor(Suite(File("a", FileOutcome.Timeout))));

    [Fact]
    public void AnyParse_Two() =>
        Assert.Equal(2, SuiteExit.CodeFor(Suite(File("a", FileOutcome.Passed), File("b", FileOutcome.Parse))));

    [Fact]
    public void AnyConnection_Three() =>
        Assert.Equal(3, SuiteExit.CodeFor(Suite(File("a", FileOutcome.Passed), File("b", FileOutcome.Connection))));

    [Fact]
    public void Connection_Outranks_ParseAndFailed_Three() =>
        Assert.Equal(3, SuiteExit.CodeFor(Suite(
            File("a", FileOutcome.Failed),
            File("b", FileOutcome.Parse),
            File("c", FileOutcome.Connection))));

    [Fact]
    public void Parse_Outranks_Failed_Two() =>
        Assert.Equal(2, SuiteExit.CodeFor(Suite(File("a", FileOutcome.Failed), File("b", FileOutcome.Parse))));

    [Fact]
    public void FailedOutranksSkipped_One() =>
        Assert.Equal(1, SuiteExit.CodeFor(Suite(File("a", FileOutcome.Skipped), File("b", FileOutcome.Failed))));

    [Theory]
    [InlineData(FileOutcome.Passed, true)]
    [InlineData(FileOutcome.Skipped, true)]
    [InlineData(FileOutcome.Failed, false)]
    [InlineData(FileOutcome.Timeout, false)]
    [InlineData(FileOutcome.Connection, false)]
    [InlineData(FileOutcome.Parse, false)]
    public void SuiteOk_TrueOnlyWhenAllPassedOrSkipped(FileOutcome outcome, bool expectedOk) =>
        Assert.Equal(expectedOk, Suite(File("a", FileOutcome.Passed), File("b", outcome)).Ok);
}
