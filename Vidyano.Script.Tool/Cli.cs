using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Spectre.Console;
using Vidyano.Script;
using Vidyano.Script.Runtime;

namespace Vidyano.Script.Tool;

/// <summary>
/// Top-level command dispatcher. Kept hand-rolled (no System.CommandLine dependency) because the
/// surface is small and the parser is what users skim in <c>--help</c> — every layer in between costs
/// readability. Subcommands are pure static methods; shared option parsing lives in <see cref="Args"/>.
/// </summary>
public static class Cli
{
    /// <summary>Exit code: everything passed.</summary>
    public const int ExitOk = 0;
    /// <summary>Exit code: one or more assertions or guard checks failed at runtime.</summary>
    public const int ExitFail = 1;
    /// <summary>Exit code: the script could not be parsed.</summary>
    public const int ExitLint = 2;
    /// <summary>Exit code: connection or sign-in failed before any script work happened.</summary>
    public const int ExitConnect = 3;
    /// <summary>Exit code: bad CLI usage (unknown subcommand, missing argument).</summary>
    public const int ExitUsage = 64;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return ExitOk;
        }

        var sub = args[0];
        var rest = args[1..];

        // `--help` and `-h` short-circuit to usage even when used without a subcommand.
        if (sub is "--help" or "-h")
        {
            PrintUsage();
            return ExitOk;
        }

        return sub switch
        {
            "run"   => await RunCommand.ExecuteAsync(rest).ConfigureAwait(false),
            "lint"  => await LintCommand.ExecuteAsync(rest).ConfigureAwait(false),
            "repl"  => await ReplCommand.ExecuteAsync(rest).ConfigureAwait(false),
            "help"  => Help(rest),
            _ => UnknownCommand(sub),
        };
    }

    private static int UnknownCommand(string sub)
    {
        AnsiConsole.MarkupLine($"[red]Unknown command:[/] [yellow]{Markup.Escape(sub)}[/]");
        var hint = Diagnostics.Suggester.Hint(sub, new[] { "run", "lint", "repl", "help" });
        if (hint != null) AnsiConsole.MarkupLine($"[grey]{Markup.Escape(hint)}[/]");
        AnsiConsole.WriteLine();
        PrintUsage();
        return ExitUsage;
    }

    private static int Help(string[] args)
    {
        if (args.Length > 0 && args[0] == "verbs")
        {
            VerbReference.Print();
            return ExitOk;
        }
        PrintUsage();
        return ExitOk;
    }

    internal static bool IsHelpFlag(string s) => s is "-h" or "--help";

    private static void PrintUsage()
    {
        AnsiConsole.MarkupLine("[bold]vidyano[/] — run .visc scripts against a Vidyano service");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Usage:[/]");
        AnsiConsole.MarkupLine("  vidyano [yellow]run[/]   <file.visc> [grey][[options]][/]   Execute a script.");
        AnsiConsole.MarkupLine("  vidyano [yellow]lint[/]  <file.visc>                  Parse-check without executing.");
        AnsiConsole.MarkupLine("  vidyano [yellow]repl[/]  [grey][[options]][/]                     Start an interactive .visc REPL.");
        AnsiConsole.MarkupLine("  vidyano [yellow]help[/]  [grey][[verbs]][/]                      Show help. 'verbs' lists every .visc verb.");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Options shared by run/repl:[/]");
        AnsiConsole.MarkupLine("  [green]--app[/] <uri>            Base URI of the Vidyano service (overrides script's @app).");
        AnsiConsole.MarkupLine("  [green]--var[/] key=value        Pre-seed a script variable. Repeatable.");
        AnsiConsole.MarkupLine("  [green]--mode[/] navigation|audit|direct");
        AnsiConsole.MarkupLine("                          Guard mode (overrides script's @mode).");
        AnsiConsole.MarkupLine("  [green]--tools[/] <path.dll>      Load an external tool pack (IVidyanoScriptToolPack). Repeatable.");
        AnsiConsole.MarkupLine("  [green]--seed[/] <int>           Fix the {{@uuid}}/{{@random}} sequence (next value per reference).");
        AnsiConsole.MarkupLine("  [green]--now[/] <iso-datetime>   Anchor the run clock for {{@today}}/{{@now}} (then flows by real elapsed).");
        AnsiConsole.MarkupLine("  [green]--json[/]                   NDJSON output (one event per line).");
        AnsiConsole.MarkupLine("  [green]--verbose[/]                Show per-statement snapshot detail.");
        AnsiConsole.MarkupLine("  [green]--insecure[/]               Bypass TLS validation (local dev certs only).");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Exit codes:[/]  0 ok, 1 failed, 2 parse error, 3 connection error, 64 usage.");
    }
}
