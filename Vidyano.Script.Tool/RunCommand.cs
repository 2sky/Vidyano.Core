using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using Vidyano.Script;
using Vidyano.Script.Diagnostics;
using Vidyano.Script.Runtime;

namespace Vidyano.Script.Tool;

/// <summary><c>vidyano run &lt;file&gt;</c> — execute a .visc and render the result.</summary>
public static class RunCommand
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

        if (a.File is null)
        {
            AnsiConsole.MarkupLine("[red]error:[/] missing <file>. Usage: [yellow]vidyano run <file.visc>[/]");
            return Cli.ExitUsage;
        }

        if (!File.Exists(a.File))
        {
            AnsiConsole.MarkupLine($"[red]error:[/] file not found: [yellow]{Markup.Escape(a.File)}[/]");
            return Cli.ExitUsage;
        }

        var options = a.ToOptions();
        if (a.ToolPaths.Count > 0)
        {
            try
            {
                var packs = ToolPackLoader.LoadInto(a.ToolPaths, options);
                if (a.Verbose && !a.Json)
                {
                    foreach (var p in packs)
                        AnsiConsole.MarkupLine(
                            $"[grey]loaded[/] [yellow]{Markup.Escape(p.PackTypeName)}[/] " +
                            $"({p.ToolNames.Count} tool(s): {Markup.Escape(string.Join(", ", p.ToolNames))})");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]error:[/] {Markup.Escape(ex.Message)}");
                return Cli.ExitUsage;
            }
        }

        var result = await VidyanoScript.RunFileAsync(a.File, options).ConfigureAwait(false);

        if (a.Json)
            JsonReporter.Write(result);
        else
            ConsoleReporter.Write(result, a.Verbose);

        if (result.ParseDiagnostics.Count > 0) return Cli.ExitLint;
        return result.Ok ? Cli.ExitOk : Cli.ExitFail;
    }
}
