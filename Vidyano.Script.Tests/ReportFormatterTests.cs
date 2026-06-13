using System;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Vidyano.Script.Runtime;
using Vidyano.Script.Runtime.Reporting;
using Xunit;
using static Vidyano.Script.Tests.SuiteTestData;

namespace Vidyano.Script.Tests;

/// <summary>Asserts the exact shape of each machine-readable report for a fabricated pass/fail/skip mix —
/// no disk, no network. The formatters are pure, so the bytes are deterministic and re-parseable.</summary>
public sealed class ReportFormatterTests
{
    private static SuiteResult Mixed() => Suite(
        FromScript(Passing("a.visc")),
        FromScript(Failing("b.visc", message: "Plate already exists")),
        FromScript(AllSkipped("c.visc")));

    // ---- JUnit ----------------------------------------------------------------------------------

    [Fact]
    public void JUnit_HasXmlDeclaration_Utf8()
    {
        var xml = new JUnitFormatter().Render(Mixed()).Text;
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml);
        Assert.DoesNotContain("\r\n", xml); // deterministic LF endings
    }

    [Fact]
    public void JUnit_RootTallies_MatchTheMix()
    {
        var doc = XDocument.Parse(new JUnitFormatter().Render(Mixed()).Text);
        var root = doc.Root!;
        Assert.Equal("testsuites", root.Name.LocalName);
        Assert.Equal("vidyano", (string)root.Attribute("name")!);
        Assert.Equal("3", (string)root.Attribute("tests")!);    // one testcase per step, one step per file
        Assert.Equal("1", (string)root.Attribute("failures")!);
        Assert.Equal("0", (string)root.Attribute("errors")!);
        Assert.Equal("1", (string)root.Attribute("skipped")!);
    }

    [Fact]
    public void JUnit_FailingFile_HasFailureWithMessageAndType()
    {
        var doc = XDocument.Parse(new JUnitFormatter().Render(Mixed()).Text);
        var failure = doc.Descendants("testsuite")
            .Single(s => (string)s.Attribute("name")! == "b.visc")
            .Descendants("failure").Single();
        Assert.Equal("Plate already exists", (string)failure.Attribute("message")!);
        Assert.Equal("assert-failed", (string)failure.Attribute("type")!);
    }

    [Fact]
    public void JUnit_SkippedFile_HasSkippedElement()
    {
        var doc = XDocument.Parse(new JUnitFormatter().Render(Mixed()).Text);
        var suite = doc.Descendants("testsuite").Single(s => (string)s.Attribute("name")! == "c.visc");
        Assert.Single(suite.Descendants("skipped"));
    }

    [Fact]
    public void JUnit_FileWithNoResult_BecomesErrorTestcase()
    {
        // A timeout/connection file (null Script) contributes one synthetic <error> testcase.
        var suite = Suite(new FileResult("t.visc", FileOutcome.Timeout, null, TimeSpan.Zero, "timed out after 5s"));
        var doc = XDocument.Parse(new JUnitFormatter().Render(suite).Text);
        var error = doc.Descendants("error").Single();
        Assert.Equal("timed out after 5s", (string)error.Attribute("message")!);
        Assert.Equal("1", (string)doc.Root!.Attribute("errors")!);
    }

    // ---- TAP ------------------------------------------------------------------------------------

    [Fact]
    public void Tap_EmitsVersionPlanAndPoints()
    {
        var tap = new TapFormatter().Render(Mixed()).Text;
        var lines = tap.Split('\n');
        Assert.Equal("TAP version 13", lines[0]);
        Assert.Equal("1..3", lines[1]);
        Assert.Contains("ok 1 - a.visc", tap);
        Assert.Contains("not ok 2 - b.visc", tap);
        Assert.Contains("ok 3 - c.visc # SKIP", tap);
        Assert.Contains("message: \"Plate already exists\"", tap);
    }

    // ---- SARIF ----------------------------------------------------------------------------------

    [Fact]
    public void Sarif_IsValidJson_WithSchemaAndResults()
    {
        var sarif = new SarifFormatter().Render(Mixed()).Text;
        using var doc = JsonDocument.Parse(sarif);
        var root = doc.RootElement;
        Assert.Equal("2.1.0", root.GetProperty("version").GetString());
        Assert.True(root.TryGetProperty("$schema", out _));

        var run = root.GetProperty("runs")[0];
        Assert.Equal("vidyano", run.GetProperty("tool").GetProperty("driver").GetProperty("name").GetString());

        var results = run.GetProperty("results");
        Assert.Equal(1, results.GetArrayLength()); // only the failing file contributes a finding
        Assert.Equal("assert-failed", results[0].GetProperty("ruleId").GetString());
        Assert.Equal("Plate already exists", results[0].GetProperty("message").GetProperty("text").GetString());
    }
}
