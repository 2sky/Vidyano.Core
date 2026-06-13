using System;
using System.IO;
using System.Threading.Tasks;
using Spectre.Console;
using Vidyano.Script;
using Vidyano.Script.Runtime;
using Vidyano.Script.Runtime.Reporting;

namespace Vidyano.Script.Tool;

/// <summary><c>vidyano test &lt;path...&gt;</c> — discover and run a suite of .visc files, render the result,
/// optionally emit JUnit/TAP/SARIF reports, and return an aggregate exit code.</summary>
public static class TestCommand
{
    public static async Task<int> ExecuteAsync(string[] args)
    {
        var a = Args.Parse(args);

        if (a.Unknown.Count > 0)
        {
            foreach (var u in a.Unknown) AnsiConsole.MarkupLine($"[red]error:[/] {Markup.Escape(u)}");
            AnsiConsole.MarkupLine("Run [yellow]vidyano help[/] for usage.");
            return Cli.ExitUsage;
        }

        if (a.Paths.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]error:[/] missing <path>. Usage: [yellow]vidyano test <file|dir|glob>...[/]");
            return Cli.ExitUsage;
        }

        // Build a template options once (including loading any --tools packs); each file gets a fresh copy so
        // concurrent runs never share the variable/tool tables.
        var template = a.ToOptions();
        if (a.ToolPaths.Count > 0)
        {
            try { ToolPackLoader.LoadInto(a.ToolPaths, template); }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]error:[/] {Markup.Escape(ex.Message)}");
                return Cli.ExitUsage;
            }
        }

        var sources = SourceDiscovery.Discover(a.Paths);
        if (sources.Count == 0)
        {
            // A passing run of zero tests is the classic CI lie — fail loudly instead.
            AnsiConsole.MarkupLine($"[red]error:[/] no .visc files found in: [yellow]{Markup.Escape(string.Join(", ", a.Paths))}[/]");
            return Cli.ExitUsage;
        }

        var runOptions = new SuiteRunOptions
        {
            MaxParallelism = a.Jobs ?? 1,
            PerFileTimeout = a.Timeout,
        };

        var suite = await VidyanoScript.RunSuiteAsync(
            sources,
            _ => new VidyanoScriptOptions(template),
            runOptions).ConfigureAwait(false);

        if (a.Json)
            SuiteJsonReporter.Write(suite);
        else
            SuiteConsoleReporter.Write(suite, a.Verbose);

        foreach (var report in a.Reports)
            EmitReport(report, suite, a.Json);

        return SuiteExit.CodeFor(suite);
    }

    private static void EmitReport(ReportSpec report, SuiteResult suite, bool json)
    {
        IReportFormatter formatter = report.Format switch
        {
            "junit" => new JUnitFormatter(),
            "tap"   => new TapFormatter(),
            "sarif" => new SarifFormatter(),
            _       => new JUnitFormatter(), // unreachable: Args validated the format
        };

        var artifact = formatter.Render(suite);
        if (report.Target is { } path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, artifact.Text);
            if (!json) AnsiConsole.MarkupLine($"[grey]wrote {report.Format} report → {Markup.Escape(path)}[/]");
        }
        else
        {
            // No target: write to stdout (a report piped straight to another tool).
            Console.Out.Write(artifact.Text);
            if (artifact.Text.Length > 0 && artifact.Text[artifact.Text.Length - 1] != '\n')
                Console.Out.WriteLine();
        }
    }
}
