using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Vidyano.Script.Diagnostics;

namespace Vidyano.Script.Runtime.Reporting;

/// <summary>
/// Renders a suite as JUnit XML — the format CI dashboards (GitLab, Jenkins, GitHub test-reporter) read.
/// Each file becomes a <c>&lt;testsuite&gt;</c>; each <c>###</c> step becomes a <c>&lt;testcase&gt;</c>. Files
/// that never ran (parse / connection / timeout) contribute a single synthetic <c>&lt;testcase&gt;</c> carrying
/// an <c>&lt;error&gt;</c>. Output is deterministic (no timestamps, no hostname, invariant durations, LF
/// line endings) so it is byte-assertable in a test.
/// </summary>
public sealed class JUnitFormatter : IReportFormatter
{
    public string Format => "junit";

    private enum Kind { Pass, Skipped, Failure, Error }

    private readonly record struct Case(string Name, TimeSpan Time, Kind Kind, string? Message, string? Type, string? Detail);

    public ReportArtifact Render(SuiteResult suite)
    {
        int tests = 0, failures = 0, errors = 0, skipped = 0;
        var suiteEls = new List<XElement>(suite.Files.Count);

        foreach (var file in suite.Files)
        {
            var cases = BuildCases(file).ToList();
            int f = cases.Count(c => c.Kind == Kind.Failure);
            int e = cases.Count(c => c.Kind == Kind.Error);
            int s = cases.Count(c => c.Kind == Kind.Skipped);
            tests += cases.Count; failures += f; errors += e; skipped += s;

            suiteEls.Add(new XElement("testsuite",
                new XAttribute("name", file.Source),
                new XAttribute("tests", cases.Count),
                new XAttribute("failures", f),
                new XAttribute("errors", e),
                new XAttribute("skipped", s),
                new XAttribute("time", ReportHelpers.Seconds(file.Duration)),
                cases.Select(c => ToElement(c, file.Source))));
        }

        var root = new XElement("testsuites",
            new XAttribute("name", "vidyano"),
            new XAttribute("tests", tests),
            new XAttribute("failures", failures),
            new XAttribute("errors", errors),
            new XAttribute("skipped", skipped),
            new XAttribute("time", ReportHelpers.Seconds(suite.Duration)),
            suiteEls);

        return new ReportArtifact(Format, Serialize(root), "vidyano.junit.xml");
    }

    private static XElement ToElement(Case c, string classname)
    {
        var tc = new XElement("testcase",
            new XAttribute("name", c.Name),
            new XAttribute("classname", classname),
            new XAttribute("time", ReportHelpers.Seconds(c.Time)));
        switch (c.Kind)
        {
            case Kind.Failure:
                tc.Add(new XElement("failure",
                    new XAttribute("message", c.Message ?? ""),
                    new XAttribute("type", c.Type ?? ErrorKind.AssertFailed),
                    c.Detail ?? ""));
                break;
            case Kind.Error:
                tc.Add(new XElement("error",
                    new XAttribute("message", c.Message ?? ""),
                    new XAttribute("type", c.Type ?? ErrorKind.TransportError),
                    c.Detail ?? ""));
                break;
            case Kind.Skipped:
                tc.Add(new XElement("skipped"));
                break;
        }
        return tc;
    }

    private static IEnumerable<Case> BuildCases(FileResult file)
    {
        if (file.Script is { Steps.Count: > 0 } script)
        {
            foreach (var step in script.Steps)
            {
                var name = string.IsNullOrEmpty(step.Label) ? "(step)" : step.Label;
                if (step.Skipped)
                {
                    yield return new Case(name, step.Duration, Kind.Skipped, null, null, null);
                }
                else if (step.Ok)
                {
                    yield return new Case(name, step.Duration, Kind.Pass, null, null, null);
                }
                else
                {
                    var diags = step.Statements.Where(s => !s.Ok && !s.Skipped).SelectMany(s => s.Diagnostics).ToList();
                    var first = diags.FirstOrDefault();
                    var kind = ReportHelpers.IsAssertish(first?.Kind) ? Kind.Failure : Kind.Error;
                    yield return new Case(name, step.Duration, kind,
                        first?.Message ?? "step failed", first?.Kind,
                        string.Join("\n", diags.Select(ReportHelpers.OneLine)));
                }
            }
            yield break;
        }

        // No steps (parse error) or no result at all (timeout / connection): one synthetic case for the file.
        var fd = ReportHelpers.FailureDiagnostics(file.Script ?? Empty).ToList();
        var head = fd.FirstOrDefault();
        var message = file.Error ?? head?.Message ?? file.Outcome.ToString();
        var detail = fd.Count > 0 ? string.Join("\n", fd.Select(ReportHelpers.OneLine)) : file.Error;
        yield return new Case(file.Source, file.Duration, Kind.Error, message, head?.Kind, detail);
    }

    private static readonly ScriptResult Empty =
        new("", false, Array.Empty<StepResult>(), Array.Empty<Diagnostic>());

    private static string Serialize(XElement root)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            OmitXmlDeclaration = false,
            Encoding = System.Text.Encoding.UTF8,
        };
        using var sw = new ReportHelpers.Utf8StringWriter();
        using (var xw = XmlWriter.Create(sw, settings))
            new XDocument(root).Save(xw);
        return sw.ToString();
    }
}
