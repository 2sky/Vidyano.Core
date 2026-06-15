using System.Linq;
using System.Text;

namespace Vidyano.Script.Runtime.Reporting;

/// <summary>
/// Renders a suite as TAP version 13 — one test point per file. Passed / Skipped files are <c>ok</c>
/// (skips carry a <c># SKIP</c> directive); everything else is <c>not ok</c> with a YAML diagnostic block.
/// Flat by design: TAP has no nesting, and per-file granularity is what TAP consumers expect. LF endings,
/// no timestamps — deterministic.
/// </summary>
public sealed class TapFormatter : IReportFormatter
{
    public string Format => "tap";

    public ReportArtifact Render(SuiteResult suite)
    {
        var sb = new StringBuilder();
        sb.Append("TAP version 13\n");
        sb.Append("1..").Append(suite.Files.Count).Append('\n');

        var n = 0;
        foreach (var file in suite.Files)
        {
            n++;
            var ok = file.Outcome is FileOutcome.Passed or FileOutcome.Skipped;
            sb.Append(ok ? "ok " : "not ok ").Append(n).Append(" - ").Append(file.Source);
            if (file.Outcome == FileOutcome.Skipped)
                sb.Append(" # SKIP all statements skipped");
            sb.Append('\n');

            if (!ok)
            {
                var message = FirstMessage(file);
                sb.Append("  ---\n");
                sb.Append("  message: ").Append(YamlScalar(message)).Append('\n');
                sb.Append("  severity: ").Append(file.Outcome.ToString().ToLowerInvariant()).Append('\n');
                sb.Append("  ...\n");
            }
        }

        return new ReportArtifact(Format, sb.ToString(), "vidyano.tap");
    }

    private static string FirstMessage(FileResult file)
    {
        if (file.Error is { Length: > 0 } err) return err;
        if (file.Script is { } script)
        {
            var d = ReportHelpers.FailureDiagnostics(script).FirstOrDefault();
            if (d is not null) return d.Message;
        }
        return file.Outcome.ToString();
    }

    // Single-line, double-quoted YAML scalar: escape backslash and quote so a message with a colon or
    // quote stays valid YAML on one line.
    private static string YamlScalar(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "") + "\"";
}
