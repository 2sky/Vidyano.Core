using System.Linq;
using Vidyano.Script.Runtime;

namespace Vidyano.Script.Tool;

/// <summary>
/// NDJSON suite output: a <c>file.start</c> / (the existing single-file <c>step.*</c>/<c>statement.*</c>
/// events from <see cref="JsonReporter"/>) / <c>file.end</c> frame per file, then a final <c>suite.summary</c>.
/// Wrapping the documented per-file schema rather than replacing it keeps existing single-file consumers
/// working while adding the suite envelope.
/// </summary>
public static class SuiteJsonReporter
{
    public static void Write(SuiteResult suite)
    {
        foreach (var file in suite.Files)
        {
            JsonReporter.WriteEvent(new { type = "file.start", source = file.Source });
            if (file.Script is { } script)
                JsonReporter.Write(script);                       // the documented per-file event stream
            else
                JsonReporter.WriteEvent(new { type = "file.error", source = file.Source, error = file.Error });
            JsonReporter.WriteEvent(new
            {
                type = "file.end",
                source = file.Source,
                outcome = file.Outcome.ToString(),
                durationMs = (long)file.Duration.TotalMilliseconds,
            });
        }

        int Count(FileOutcome o) => suite.Files.Count(f => f.Outcome == o);
        JsonReporter.WriteEvent(new
        {
            type = "suite.summary",
            files = suite.Files.Count,
            passed = Count(FileOutcome.Passed),
            failed = Count(FileOutcome.Failed),
            skipped = Count(FileOutcome.Skipped),
            timeout = Count(FileOutcome.Timeout),
            connection = Count(FileOutcome.Connection),
            parse = Count(FileOutcome.Parse),
            ok = suite.Ok,
            exitCode = SuiteExit.CodeFor(suite),
            durationMs = (long)suite.Duration.TotalMilliseconds,
        });
    }
}
