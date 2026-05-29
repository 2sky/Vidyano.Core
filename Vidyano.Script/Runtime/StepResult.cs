using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Parsing;

namespace Vidyano.Script.Runtime;

/// <summary>
/// Outcome of running one parsed <see cref="Statement"/>. The interpreter emits one of these per
/// statement; <see cref="ScriptResult"/> aggregates them per step and per script.
/// </summary>
public sealed record StatementResult(
    Statement Statement,
    bool Ok,
    Snapshot? Snapshot,
    IReadOnlyList<Diagnostic> Diagnostics,
    bool Skipped = false);

/// <summary>Aggregate of all <see cref="StatementResult"/>s in one <see cref="Step"/>.
/// <see cref="Skipped"/> is <c>true</c> when the step had statements and every one was skipped.</summary>
public sealed record StepResult(
    string Label,
    SourceLocation Location,
    bool Ok,
    IReadOnlyList<StatementResult> Statements,
    bool Skipped = false);

/// <summary>Top-level outcome of running a whole .visc.</summary>
public sealed record ScriptResult(
    string SourcePath,
    bool Ok,
    IReadOnlyList<StepResult> Steps,
    IReadOnlyList<Diagnostic> ParseDiagnostics)
{
    /// <summary>
    /// Renders a plain-text, human-readable report of this run: a header line with the source path
    /// and a pass/fail/skip tally, then every parse diagnostic and — for each failed step — its
    /// failed statements with location, message, and hint. Skipped statements (unmet <c>REQUIRES</c>)
    /// are counted but not expanded; passing statements are summarized in the tally only.
    /// </summary>
    /// <remarks>
    /// Built for assertion messages and logs — e.g. <c>Assert.That(result.Ok, Is.True, result.Describe())</c>
    /// — so every consumer gets the same report instead of re-deriving it. The record's own
    /// <see cref="object.ToString"/> is intentionally left as the structural dump for debugging.
    /// </remarks>
    public string Describe()
    {
        var sb = new StringBuilder();

        if (ParseDiagnostics.Count > 0)
        {
            sb.AppendLine($"visc parse failed: {SourcePath}");
            foreach (var d in ParseDiagnostics)
                AppendDiagnostic(sb, d);
            return sb.ToString().TrimEnd();
        }

        var statements = Steps.SelectMany(s => s.Statements).ToList();
        var failed = statements.Count(s => !s.Ok && !s.Skipped);
        var skipped = statements.Count(s => s.Skipped);
        var passed = statements.Count - failed - skipped;

        var tally = $"{passed}/{statements.Count} ok";
        if (failed > 0) tally += $", {failed} failed";
        if (skipped > 0) tally += $", {skipped} skipped";
        sb.AppendLine($"visc {(Ok ? "ok" : "failed")}: {SourcePath}  ({tally})");

        foreach (var step in Steps.Where(s => !s.Ok))
        {
            sb.AppendLine(string.IsNullOrEmpty(step.Label) ? "  step:" : $"  step '{step.Label}':");
            foreach (var stmt in step.Statements.Where(s => !s.Ok && !s.Skipped))
                foreach (var d in stmt.Diagnostics)
                    AppendDiagnostic(sb, d, "    ");
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendDiagnostic(StringBuilder sb, Diagnostic d, string indent = "  ")
    {
        sb.AppendLine($"{indent}[{d.Kind}] {d.Location.Line}:{d.Location.Column} {d.Message}");
        if (!string.IsNullOrEmpty(d.Hint))
            sb.AppendLine($"{indent}  hint: {d.Hint}");
    }
}
