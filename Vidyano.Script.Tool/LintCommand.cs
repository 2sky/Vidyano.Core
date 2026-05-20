using System.IO;
using System.Threading.Tasks;
using Spectre.Console;
using Vidyano.Script;

namespace Vidyano.Script.Tool;

/// <summary><c>vidyano lint &lt;file&gt;</c> — parse-check a .visc without executing it.</summary>
public static class LintCommand
{
    public static Task<int> ExecuteAsync(string[] args)
    {
        if (args.Length == 0 || Cli.IsHelpFlag(args[0]))
        {
            AnsiConsole.MarkupLine("Usage: [yellow]vidyano lint <file.visc>[/]");
            return Task.FromResult(Cli.ExitUsage);
        }

        var path = args[0];
        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLine($"[red]error:[/] file not found: [yellow]{Markup.Escape(path)}[/]");
            return Task.FromResult(Cli.ExitUsage);
        }

        var diags = VidyanoScript.Lint(File.ReadAllText(path), path);
        if (diags.Count == 0)
        {
            AnsiConsole.MarkupLine($"[green]ok[/] {Markup.Escape(path)} (no problems)");
            return Task.FromResult(Cli.ExitOk);
        }

        foreach (var d in diags)
            ConsoleReporter.WriteDiagnostic(d);
        AnsiConsole.MarkupLine($"[red]{diags.Count} problem(s)[/] in {Markup.Escape(path)}");
        return Task.FromResult(Cli.ExitLint);
    }
}
