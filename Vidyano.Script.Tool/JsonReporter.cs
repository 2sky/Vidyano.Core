using System;
using System.Linq;
using System.Text.Json;
using Vidyano.Script.Runtime;

namespace Vidyano.Script.Tool;

/// <summary>
/// NDJSON output: one JSON object per line — <c>step.start</c>, <c>statement.result</c>,
/// <c>step.end</c>, then a final <c>run.summary</c>. Stream-parseable so an agent can react before
/// the run completes. The schema is the single source of truth for machine consumers.
/// </summary>
public static class JsonReporter
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static void Write(ScriptResult result)
    {
        // Parse-only failures: surface every diagnostic and stop.
        foreach (var pd in result.ParseDiagnostics)
            WriteEvent(new { type = "parse.diagnostic", diagnostic = pd });

        foreach (var step in result.Steps)
        {
            WriteEvent(new { type = "step.start", label = step.Label, location = step.Location });
            foreach (var s in step.Statements)
                WriteEvent(new
                {
                    type = "statement.result",
                    ok = s.Ok,
                    skipped = s.Skipped,
                    location = s.Statement.Location,
                    statementType = s.Statement.GetType().Name,
                    snapshot = s.Snapshot,
                    diagnostics = s.Diagnostics,
                });
            WriteEvent(new { type = "step.end", label = step.Label, ok = step.Ok });
        }

        WriteEvent(new
        {
            type = "run.summary",
            sourcePath = result.SourcePath,
            ok = result.Ok,
            stepCount = result.Steps.Count,
            failed = result.Steps.SelectMany(s => s.Statements).Count(s => !s.Ok),
            skipped = result.Steps.SelectMany(s => s.Statements).Count(s => s.Skipped),
        });
    }

    private static void WriteEvent(object payload)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(payload, Opts));
    }
}
