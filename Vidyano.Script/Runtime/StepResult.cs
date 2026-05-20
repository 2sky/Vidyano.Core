using System.Collections.Generic;
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
    IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>Aggregate of all <see cref="StatementResult"/>s in one <see cref="Step"/>.</summary>
public sealed record StepResult(
    string Label,
    SourceLocation Location,
    bool Ok,
    IReadOnlyList<StatementResult> Statements);

/// <summary>Top-level outcome of running a whole .visc.</summary>
public sealed record ScriptResult(
    string SourcePath,
    bool Ok,
    IReadOnlyList<StepResult> Steps,
    IReadOnlyList<Diagnostic> ParseDiagnostics);
