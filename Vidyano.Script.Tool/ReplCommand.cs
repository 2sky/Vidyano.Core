using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using Vidyano.Script;
using Vidyano.Script.Parsing;
using Vidyano.Script.Runtime;

namespace Vidyano.Script.Tool;

/// <summary>
/// <c>vidyano repl</c> — an interactive .visc prompt. The point isn't just exploration; it's that
/// you can <c>:save</c> the session history as a real .visc when you're done, so "I poked at it"
/// becomes "I committed a test" without retyping. Anything that runs in the REPL runs identically
/// when the same lines are saved and executed via <c>vidyano run</c>.
/// </summary>
public static class ReplCommand
{
    public static async Task<int> ExecuteAsync(string[] args)
    {
        var a = Args.Parse(args);
        if (a.Unknown.Count > 0)
        {
            foreach (var u in a.Unknown) AnsiConsole.MarkupLine($"[red]error:[/] {Markup.Escape(u)}");
            return Cli.ExitUsage;
        }
        if (string.IsNullOrEmpty(a.AppUri))
        {
            AnsiConsole.MarkupLine("[red]error:[/] [yellow]--app <uri>[/] is required for the REPL.");
            AnsiConsole.MarkupLine("Example: [yellow]vidyano repl --app https://demo.vidyano.com[/]");
            return Cli.ExitUsage;
        }

        var options = a.ToOptions();
        if (a.ToolPaths.Count > 0)
        {
            try
            {
                var packs = ToolPackLoader.LoadInto(a.ToolPaths, options);
                foreach (var p in packs)
                    AnsiConsole.MarkupLine(
                        $"[grey]loaded[/] [yellow]{Markup.Escape(p.PackTypeName)}[/] " +
                        $"({p.ToolNames.Count} tool(s): {Markup.Escape(string.Join(", ", p.ToolNames))})");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]error:[/] {Markup.Escape(ex.Message)}");
                return Cli.ExitUsage;
            }
        }

        using var session = new VidyanoSession(a.AppUri, acceptAnyServerCertificate: a.Insecure);
        var interpreter = new Interpreter(session, options.Variables, a.Mode ?? GuardMode.Navigation, options.Tools, now: options.Now, seed: options.Seed);

        AnsiConsole.MarkupLine($"[bold]vidyano repl[/] — connected to [green]{Markup.Escape(a.AppUri)}[/]");
        AnsiConsole.MarkupLine("[grey]Type a .visc line at the prompt. ':help' lists commands. ':save <path>' to write history. Ctrl-C exits.[/]");

        var history = new List<string>();

        while (true)
        {
            Console.Write("visc> ");
            var line = Console.ReadLine();
            if (line is null) break;
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Meta commands
            if (trimmed.StartsWith(':'))
            {
                if (!await HandleMeta(trimmed, history, interpreter, session).ConfigureAwait(false)) break;
                continue;
            }

            // Parse + run the single line as a one-step script.
            var lexer = new Lexer(line, "<repl>");
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens, lexer.Diagnostics);
            var script = parser.Parse();
            if (parser.Diagnostics.Any())
            {
                foreach (var d in parser.Diagnostics) ConsoleReporter.WriteDiagnostic(d);
                continue;
            }
            var result = await interpreter.RunAsync(script).ConfigureAwait(false);
            foreach (var step in result.Steps)
                foreach (var s in step.Statements)
                {
                    if (s.Ok)
                        AnsiConsole.MarkupLine($"[green]ok[/] {ConsoleReporter.Describe(s.Statement)}");
                    else
                        foreach (var d in s.Diagnostics) ConsoleReporter.WriteDiagnostic(d);
                }

            // Only record lines that parsed; failed-at-runtime lines stay in history so :save replays
            // the same scenario (intentional — captures user intent, not just successes).
            history.Add(line);
        }
        return Cli.ExitOk;
    }

    private static Task<bool> HandleMeta(string cmd, List<string> history, Interpreter interpreter, VidyanoSession session)
    {
        // Returns false to exit the REPL.
        var parts = cmd.Split(' ', 2);
        switch (parts[0])
        {
            case ":help":
                AnsiConsole.MarkupLine("[bold]REPL commands:[/]");
                AnsiConsole.MarkupLine("  [yellow]:save <path>[/]    Write the session history as a .visc file.");
                AnsiConsole.MarkupLine("  [yellow]:load <path>[/]    Run a .visc file in this session.");
                AnsiConsole.MarkupLine("  [yellow]:vars[/]           List current script variables.");
                AnsiConsole.MarkupLine("  [yellow]:snapshot[/]       Print the current session snapshot.");
                AnsiConsole.MarkupLine("  [yellow]:verbs[/]          List every .visc verb.");
                AnsiConsole.MarkupLine("  [yellow]:quit[/]           Exit.");
                return Task.FromResult(true);
            case ":quit":
            case ":exit":
                return Task.FromResult(false);
            case ":vars":
                foreach (var v in interpreter.Variables.OrderBy(kv => kv.Key))
                    AnsiConsole.MarkupLine($"  @{Markup.Escape(v.Key)} = {Markup.Escape(v.Value?.ToString() ?? "null")}");
                return Task.FromResult(true);
            case ":snapshot":
                {
                    var snap = session.TakeSnapshot();
                    System.Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(snap,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    return Task.FromResult(true);
                }
            case ":verbs":
                VerbReference.Print();
                return Task.FromResult(true);
            case ":save":
                {
                    if (parts.Length < 2) { AnsiConsole.MarkupLine("[red]usage:[/] :save <path>"); return Task.FromResult(true); }
                    File.WriteAllLines(parts[1].Trim(), history);
                    AnsiConsole.MarkupLine($"[green]wrote[/] {Markup.Escape(parts[1].Trim())} ({history.Count} line(s))");
                    return Task.FromResult(true);
                }
            case ":load":
                {
                    if (parts.Length < 2) { AnsiConsole.MarkupLine("[red]usage:[/] :load <path>"); return Task.FromResult(true); }
                    return LoadAsync(parts[1].Trim(), interpreter);
                }
            default:
                AnsiConsole.MarkupLine($"[red]unknown REPL command:[/] {Markup.Escape(parts[0])}");
                return Task.FromResult(true);
        }
    }

    private static async Task<bool> LoadAsync(string path, Interpreter interpreter)
    {
        if (!File.Exists(path)) { AnsiConsole.MarkupLine($"[red]not found:[/] {Markup.Escape(path)}"); return true; }
        var src = File.ReadAllText(path);
        var lexer = new Lexer(src, path);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens, lexer.Diagnostics);
        var script = parser.Parse();
        if (parser.Diagnostics.Any())
        {
            foreach (var d in parser.Diagnostics) ConsoleReporter.WriteDiagnostic(d);
            return true;
        }
        var result = await interpreter.RunAsync(script).ConfigureAwait(false);
        ConsoleReporter.Write(result, verbose: false);
        return true;
    }
}
