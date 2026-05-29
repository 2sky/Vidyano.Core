using System;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Parsing;
using Vidyano.Script.Runtime;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Coverage for <see cref="ScriptResult.Describe"/> — the plain-text failure report that consumers
/// (NUnit/xUnit hosts, logs) use as an assertion message. Built directly from the public result types
/// so no server is involved.
/// </summary>
public sealed class ScriptResultDescribeTests
{
    private static readonly SourceLocation Loc = new("<test>", 3, 5);

    [Fact]
    public void Describe_ParseFailure_ListsEveryParseDiagnostic()
    {
        var d = new Diagnostic(ErrorKind.ParseExpected, "ACTION needs an action name.", Loc, Hint: "ACTION Approve");
        var result = new ScriptResult("script.visc", false, Array.Empty<StepResult>(), new[] { d });

        var report = result.Describe();

        Assert.Contains("parse failed", report);
        Assert.Contains("script.visc", report);
        Assert.Contains("parse-expected", report);
        Assert.Contains("ACTION needs an action name.", report);
        Assert.Contains("hint: ACTION Approve", report);
    }

    [Fact]
    public void Describe_FailedStep_ShowsTallyStepAndDiagnostic()
    {
        var passing = new StatementResult(new SaveStmt(null, Loc), true, null, Array.Empty<Diagnostic>());
        var failing = new StatementResult(
            new SaveStmt(null, Loc),
            false,
            null,
            new[] { new Diagnostic(ErrorKind.AssertNotificationError, "Plate already exists", Loc) });
        var step = new StepResult("Insert car", Loc, false, new[] { passing, failing });
        var result = new ScriptResult("car.visc", false, new[] { step }, Array.Empty<Diagnostic>());

        var report = result.Describe();

        Assert.Contains("visc failed: car.visc", report);
        Assert.Contains("1/2 ok, 1 failed", report);
        Assert.Contains("step 'Insert car':", report);
        Assert.Contains("assert-notification-error", report);
        Assert.Contains("Plate already exists", report);
    }

    [Fact]
    public void Describe_SkippedStatements_AreTalliedNotExpanded()
    {
        // An unmet REQUIRES records skipped statements as non-failing passes; they belong in the
        // tally but must not be expanded as failures.
        var skipped = new StatementResult(
            new SaveStmt(null, Loc), true, null,
            new[] { new Diagnostic(ErrorKind.StateRequiresUnmet, "skipped", Loc) },
            Skipped: true);
        var step = new StepResult("Body", Loc, Ok: true, new[] { skipped }, Skipped: true);
        var result = new ScriptResult("g.visc", true, new[] { step }, Array.Empty<Diagnostic>());

        var report = result.Describe();

        Assert.Contains("visc ok: g.visc", report);
        Assert.Contains("1 skipped", report);
        // No failed step expansion for an all-ok run.
        Assert.DoesNotContain("step 'Body'", report);
    }

    [Fact]
    public void Describe_DoesNotReplaceRecordToString()
    {
        // Describe() is opt-in; the record's structural ToString() stays intact for debugging.
        var result = new ScriptResult("x.visc", true, Array.Empty<StepResult>(), Array.Empty<Diagnostic>());
        Assert.StartsWith("ScriptResult", result.ToString());
    }
}
