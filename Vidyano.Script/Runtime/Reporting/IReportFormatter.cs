namespace Vidyano.Script.Runtime.Reporting;

/// <summary>A rendered report: the <paramref name="Format"/> that produced it, the full <paramref name="Text"/>
/// (the host writes it to a file or stdout — the formatter never touches I/O), and a
/// <paramref name="SuggestedFileName"/> for when the caller asks for a report without naming a target.</summary>
public readonly record struct ReportArtifact(string Format, string Text, string SuggestedFileName);

/// <summary>
/// Turns a finished <see cref="SuiteResult"/> into a machine-readable report. Implementations are pure —
/// no clock, no file system, output a deterministic function of the input — so the exact bytes are
/// assertable in a test. Adding a format is adding an implementation; nothing else changes (the closed
/// built-in set is junit / tap / sarif, kept internal-extensible rather than a public plugin surface).
/// </summary>
public interface IReportFormatter
{
    /// <summary>Stable lowercase format token, as named on the CLI (<c>--report junit</c>).</summary>
    string Format { get; }

    ReportArtifact Render(SuiteResult suite);
}
