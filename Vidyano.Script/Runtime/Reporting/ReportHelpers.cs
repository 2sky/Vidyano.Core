using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Vidyano.Script.Diagnostics;

namespace Vidyano.Script.Runtime.Reporting;

/// <summary>Shared, side-effect-free helpers for the report formatters. Kept here so JUnit / TAP / SARIF
/// agree on what counts as an assertion failure vs an error and how durations are rendered.</summary>
internal static class ReportHelpers
{
    /// <summary>A failure that maps to a JUnit <c>&lt;failure&gt;</c> (the test ran and an expectation didn't
    /// hold) rather than an <c>&lt;error&gt;</c> (it couldn't run / blew up). Assertions and UI guards are
    /// failures; parse / resolve / state / transport problems are errors.</summary>
    public static bool IsAssertish(string? kind) =>
        kind is not null && (kind.StartsWith("assert-", StringComparison.Ordinal)
                          || kind.StartsWith("guard-", StringComparison.Ordinal));

    /// <summary>Every diagnostic that explains why a file is not green: parse diagnostics plus the diagnostics
    /// of failing (non-skipped) statements, in document order.</summary>
    public static IEnumerable<Diagnostic> FailureDiagnostics(ScriptResult r) =>
        r.ParseDiagnostics.Concat(
            r.Steps.SelectMany(s => s.Statements).Where(s => !s.Ok && !s.Skipped).SelectMany(s => s.Diagnostics));

    public static string Seconds(TimeSpan t) =>
        t.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);

    public static string OneLine(Diagnostic d) =>
        $"[{d.Kind}] {d.Location.Line}:{d.Location.Column} {d.Message}"
        + (string.IsNullOrEmpty(d.Hint) ? "" : $"  hint: {d.Hint}");

    /// <summary>A <see cref="StringWriter"/> that reports UTF-8 so an <see cref="System.Xml.XmlWriter"/>
    /// emits <c>encoding="utf-8"</c> in the declaration while still building an in-memory string.</summary>
    public sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
