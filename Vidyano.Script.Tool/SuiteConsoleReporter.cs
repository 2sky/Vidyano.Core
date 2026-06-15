using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using Vidyano.Script.Runtime;

namespace Vidyano.Script.Tool;

/// <summary>Human-readable suite output: one line per file with its outcome and duration, the failing
/// diagnostics expanded beneath failures, then a tally. The single-file counterpart is
/// <see cref="ConsoleReporter"/>; this is what <c>vidyano test</c> prints when not in <c>--json</c> mode.</summary>
public static class SuiteConsoleReporter
{
    public static void Write(SuiteResult suite, bool verbose)
    {
        foreach (var file in suite.Files)
        {
            var (glyph, color) = Glyph(file.Outcome);
            var ms = (long)file.Duration.TotalMilliseconds;
            AnsiConsole.MarkupLine($"  [{color}]{glyph}[/] {Markup.Escape(file.Source)} [grey]({ms} ms{Tag(file.Outcome)})[/]");

            if (file.Outcome is FileOutcome.Passed or FileOutcome.Skipped)
                continue;

            // Surface why it isn't green: the failing statements' diagnostics, or the file-level error.
            var diags = file.Script is { } s
                ? s.ParseDiagnostics.Concat(s.Steps.SelectMany(st => st.Statements)
                    .Where(x => !x.Ok && !x.Skipped).SelectMany(x => x.Diagnostics)).ToList()
                : new List<Vidyano.Script.Diagnostics.Diagnostic>();
            if (diags.Count > 0)
                foreach (var d in diags)
                    ConsoleReporter.WriteDiagnostic(d, indent: "    ");
            else if (!string.IsNullOrEmpty(file.Error))
                AnsiConsole.MarkupLine($"    [grey]{Markup.Escape(file.Error!)}[/]");
        }

        AnsiConsole.WriteLine();
        var counts = suite.Files.GroupBy(f => f.Outcome).ToDictionary(g => g.Key, g => g.Count());
        int N(FileOutcome o) => counts.TryGetValue(o, out var c) ? c : 0;
        var passed = N(FileOutcome.Passed);
        var parts = new List<string> { $"[bold]{passed}[/]/{suite.Files.Count} ok" };
        if (N(FileOutcome.Failed) > 0)     parts.Add($"[red]{N(FileOutcome.Failed)} failed[/]");
        if (N(FileOutcome.Timeout) > 0)    parts.Add($"[red]{N(FileOutcome.Timeout)} timed out[/]");
        if (N(FileOutcome.Connection) > 0) parts.Add($"[red]{N(FileOutcome.Connection)} connection[/]");
        if (N(FileOutcome.Parse) > 0)      parts.Add($"[red]{N(FileOutcome.Parse)} parse[/]");
        if (N(FileOutcome.Skipped) > 0)    parts.Add($"[yellow]{N(FileOutcome.Skipped)} skipped[/]");
        AnsiConsole.MarkupLine($"{string.Join(", ", parts)}  [grey]({(long)suite.Duration.TotalMilliseconds} ms)[/]");
    }

    private static (string Glyph, string Color) Glyph(FileOutcome o) => o switch
    {
        FileOutcome.Passed  => ("✓", "green"),
        FileOutcome.Skipped => ("↓", "yellow"),
        _                   => ("✗", "red"),
    };

    private static string Tag(FileOutcome o) => o switch
    {
        FileOutcome.Timeout    => ", timeout",
        FileOutcome.Connection => ", connection",
        FileOutcome.Parse      => ", parse",
        _                      => "",
    };
}
