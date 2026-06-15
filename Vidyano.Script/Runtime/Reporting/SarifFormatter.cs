using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Vidyano.Script.Diagnostics;

namespace Vidyano.Script.Runtime.Reporting;

/// <summary>
/// Renders a suite as SARIF 2.1.0 — the format GitHub code scanning and other static-analysis surfaces read.
/// Every failure diagnostic (parse errors and failing-statement diagnostics, across all files) becomes a
/// <c>result</c> with its <see cref="ErrorKind"/> as the rule id and a physical location (file + line). Output
/// is deterministic (property order fixed, LF endings) so it is byte-assertable.
/// </summary>
public sealed class SarifFormatter : IReportFormatter
{
    public string Format => "sarif";

    public ReportArtifact Render(SuiteResult suite)
    {
        var results = new List<object>();
        var ruleIds = new SortedSet<string>(System.StringComparer.Ordinal);

        foreach (var file in suite.Files)
        {
            if (file.Script is not { } script)
            {
                // No result at all (timeout / connection thrown before a ScriptResult): one synthetic result.
                var kind = file.Outcome == FileOutcome.Timeout ? "state-timeout" : ErrorKind.TransportError;
                ruleIds.Add(kind);
                results.Add(Result(kind, file.Error ?? file.Outcome.ToString(), file.Source, 0));
                continue;
            }

            foreach (var d in ReportHelpers.FailureDiagnostics(script))
            {
                ruleIds.Add(d.Kind);
                results.Add(Result(d.Kind, d.Message, file.Source, d.Location.Line));
            }
        }

        // A dictionary root lets us emit the literal "$schema" key (System.Text.Json can't project it from a
        // C# identifier) without string-patching the serialized JSON — patching could corrupt a diagnostic
        // message that happened to contain the substring "schema":. Insertion order is the emit order.
        var sarif = new Dictionary<string, object>
        {
            ["version"] = "2.1.0",
            ["$schema"] = "https://json.schemastore.org/sarif-2.1.0.json",
            ["runs"] = new[]
            {
                new
                {
                    tool = new
                    {
                        driver = new
                        {
                            name = "vidyano",
                            rules = ruleIds.Select(id => new { id }).ToArray(),
                        },
                    },
                    results = results.ToArray(),
                },
            },
        };

        var json = JsonSerializer.Serialize(sarif, JsonOpts).Replace("\r\n", "\n");
        return new ReportArtifact(Format, json, "vidyano.sarif");
    }

    private static object Result(string ruleId, string message, string uri, int line) => new
    {
        ruleId,
        level = "error",
        message = new { text = message },
        locations = new[]
        {
            new
            {
                physicalLocation = new
                {
                    artifactLocation = new { uri },
                    region = new { startLine = line > 0 ? line : 1 },
                },
            },
        },
    };

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
}
